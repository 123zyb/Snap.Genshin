﻿using DGP.Snap.Framework.Extensions.System;
using DGP.Snap.Framework.Extensions.System.Net;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;

namespace DGP.Snap.Framework.Net.Download
{
#nullable disable
    /// <summary>
    /// Class used for downloading files. The .NET WebClient is used for downloading.
    /// </summary>
    public class FileDownloader : IFileDownloader
    {
        private readonly IDownloadCache downloadCache;
        private readonly ManualResetEvent readyToDownload = new ManualResetEvent(true);
        private readonly System.Timers.Timer attemptTimer = new System.Timers.Timer();
        private readonly object cancelSync = new object();

        private bool isCancelled;
        private bool disposed;
        private bool useFileNameFromServer = true;
        private bool isFallback;

        private int attemptNumber;

        private string localFileName;
        private string destinationFileName;
        private string destinationFolder;

        private Uri fileSource;
        private StreamCopyWorker worker;
        private DownloadWebClient downloadWebClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileDownloader"/> class. No download cache would be used, resume is not supported
        /// </summary>
        public FileDownloader()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileDownloader"/> class.
        /// </summary>
        /// <param name="downloadCache">IDownloadCache instance</param>
        public FileDownloader(IDownloadCache downloadCache)
        {
            DnsFallbackResolver = null;

            MaxAttempts = 60;
            DelayBetweenAttempts = TimeSpan.FromSeconds(3);
            SafeWaitTimeout = TimeSpan.FromSeconds(15);
            SourceStreamReadTimeout = TimeSpan.FromSeconds(5);

            this.downloadCache = downloadCache;
            disposed = false;

            attemptTimer.Elapsed += OnDownloadAttemptTimer;
        }

        /// <summary>
        /// Fired when download is finished, even if it's failed.
        /// </summary>
        public event EventHandler<DownloadFileCompletedArgs> DownloadFileCompleted;

        /// <summary>
        /// Fired when download progress is changed.
        /// </summary>
        public event EventHandler<DownloadFileProgressChangedArgs> DownloadProgressChanged;

        /// <summary>
        /// Gets or sets the DNS fallback resolver. Default is null.
        /// </summary>
        public IDnsFallbackResolver DnsFallbackResolver { get; set; }

        /// <summary>
        /// Gets or sets the delay between download attempts. Default is 3 seconds. 
        /// </summary>
        public TimeSpan DelayBetweenAttempts { get; set; }

        /// <summary>
        /// Gets or sets the maximum waiting timeout for pending request to be finished. Default is 15 seconds.
        /// </summary>
        public TimeSpan SafeWaitTimeout { get; set; }

        /// <summary>
        /// Gets or sets the timeout for source stream. Default is 5 seconds.
        /// </summary>
        public TimeSpan SourceStreamReadTimeout { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of download attempts. Default is 60.
        /// </summary>
        public int MaxAttempts { get; set; }

        /// <summary>
        /// Gets the total bytes received so far
        /// </summary>
        public long BytesReceived { get; internal set; }

        /// <summary>
        /// Gets the total bytes to receive
        /// </summary>
        public long TotalBytesToReceive { get; internal set; }

        /// <summary>
        /// Gets or sets the time when download was started
        /// </summary>
        public DateTime DownloadStartTime { get; set; }

        private bool UseCaching => downloadCache != null;

        /// <summary>
        /// Start async download of source to destinationPath
        /// </summary>
        /// <param name="source">Source URI</param>
        /// <param name="destinationPath">Full path with file name.</param>
        public void DownloadFileAsync(Uri source, string destinationPath)
        {
            DownloadFileAsync(source, destinationPath, false);
        }

        /// <summary>
        /// Start download of source file to downloadDirectory. File would be saved with filename taken from server 
        /// </summary>
        /// <param name="source">Source URI</param>
        /// <param name="destinationDirectory">Destination directory</param>
        public void DownloadFileAsyncPreserveServerFileName(Uri source, string destinationDirectory)
        {
            DownloadFileAsync(source, Path.Combine(destinationDirectory, Guid.NewGuid().ToString()), true);
        }

        /// <summary>
        /// Cancel current download
        /// </summary>
        public void CancelDownloadAsync()
        {
            lock (cancelSync)
            {
                if (isCancelled)
                {
                    return;
                }
                isCancelled = true;
            }

            if (worker != null)
            {
                worker.Cancel();
            }

            TriggerDownloadWebClientCancelAsync();
            DeleteDownloadedFile();  ////todo: maybe this is equal to InvalidateCache? Can we get rid of DeleteDownloadedFile ?

            readyToDownload.Set();
        }

        private void DeleteDownloadedFile()
        {
            FileUtils.TryFileDelete(localFileName);
        }

        private void InvalidateCache(Uri uri)
        {
            if (!UseCaching)
            {
                return;
            }

            downloadCache.Invalidate(uri);
        }

        private void DownloadFileAsync(Uri source, string destinationPath, bool useServerFileName)
        {
            if (!WaitSafeStart())
            {
                throw new Exception("Unable to start download because another request is still in progress.");
            }

            useFileNameFromServer = useServerFileName;
            fileSource = source;
            BytesReceived = 0;
            destinationFileName = destinationPath;
            destinationFolder = Path.GetDirectoryName(destinationPath);
            isCancelled = false;
            localFileName = string.Empty;

            DownloadStartTime = DateTime.Now;

            attemptNumber = 0;

            StartDownload();
        }

        private void OnDownloadAttemptTimer(object sender, EventArgs eventArgs)
        {
            StartDownload();
        }

        private void StartDownload()
        {
            if (IsCancelled())
            {
                return;
            }

            localFileName = ComposeLocalFilename();

            if (!UseCaching)
            {
                TriggerWebClientDownloadFileAsync();
                return;
            }

            TotalBytesToReceive = -1;
            WebHeaderCollection headers = GetHttpHeaders(fileSource);
            if (headers != null)
            {
                TotalBytesToReceive = headers.GetContentLength();
            }

            if (TotalBytesToReceive == -1)
            {
                TotalBytesToReceive = 0;
                TriggerWebClientDownloadFileAsync();
            }
            else
            {
                ResumeDownload(headers);
            }
        }

        private void ResumeDownload(WebHeaderCollection headers)
        {
            isFallback = false;

            string downloadedFileName = GetDestinationFileName(headers);

            if (!FileUtils.TryGetFileSize(downloadedFileName, out long downloadedFileSize))
            {
                ////todo: handle this case in future. Now in case of error we simply proceed with downloadedFileSize=0
            }

            if (UseCaching)
            {
                downloadCache.Add(fileSource, localFileName, headers);
            }

            if (downloadedFileSize > TotalBytesToReceive)
            {
                InvalidateCache(fileSource);
            }

            if (downloadedFileSize != TotalBytesToReceive)
            {
                if (!FileUtils.ReplaceFile(downloadedFileName, localFileName))
                {
                    InvalidateCache(fileSource);
                }

                Download(fileSource, localFileName, TotalBytesToReceive);
            }
            else
            {
                DownloadFromCache(downloadedFileName);
            }
        }

        private void DownloadFromCache(string cachedResource)
        {
            OnDownloadProgressChanged(this, new DownloadFileProgressChangedArgs(100, TotalBytesToReceive, TotalBytesToReceive));
            InvokeDownloadCompleted(CompletedState.Succeeded, cachedResource, null, true);
            readyToDownload.Set();
        }

        private void TriggerWebClientDownloadFileAsync()
        {
            try
            {
                isFallback = true;
                string destinationDirectory = Path.GetDirectoryName(localFileName);
                if (destinationDirectory != null && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
                TryCleanupExistingDownloadWebClient();

                downloadWebClient = CreateWebClient();
                downloadWebClient.DownloadFileAsync(fileSource, localFileName);
            }
            catch (Exception ex)
            {
                if (!AttemptDownload())
                {
                    InvokeDownloadCompleted(CompletedState.Failed, localFileName, ex);
                }
            }
        }

        private DownloadWebClient CreateWebClient()
        {
            DownloadWebClient webClient = new DownloadWebClient();
            webClient.DownloadFileCompleted += OnDownloadCompleted;
            webClient.DownloadProgressChanged += OnDownloadProgressChanged;
            webClient.OpenReadCompleted += OnOpenReadCompleted;
            return webClient;
        }

        private void TryCleanupExistingDownloadWebClient()
        {
            if (downloadWebClient == null)
            {
                return;
            }
            try
            {
                lock (this)
                {
                    if (downloadWebClient != null)
                    {
                        downloadWebClient.DownloadFileCompleted -= OnDownloadCompleted;
                        downloadWebClient.DownloadProgressChanged -= OnDownloadProgressChanged;
                        downloadWebClient.OpenReadCompleted -= OnOpenReadCompleted;
                        downloadWebClient.CancelAsync();
                        downloadWebClient.Dispose();
                        downloadWebClient = null;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private bool AttemptDownload()
        {
            if (++attemptNumber <= MaxAttempts)
            {
                attemptTimer.Interval = DelayBetweenAttempts.TotalMilliseconds;
                attemptTimer.AutoReset = false;
                attemptTimer.Start();
                return true;
            }

            readyToDownload.Set();
            return false;
        }

        private string GetDestinationFileName(WebHeaderCollection headers)
        {
            if (!UseCaching)
            {
                return localFileName;
            }

            string cachedDestinationPath = downloadCache.Get(fileSource, headers);
            if (cachedDestinationPath == null)
            {
                //logger.Debug("No cache item found. Source: {0} Destination: {1}", fileSource, localFileName);
                DeleteDownloadedFile();
                return localFileName;
            }

            //logger.Debug("Download resource was found in cache. Source: {0} Destination: {1}", fileSource, cachedDestinationPath);
            return cachedDestinationPath;
        }

        private string ComposeLocalFilename()
        {
            if (useFileNameFromServer)
            {
                return Path.Combine(destinationFolder, string.Format("{0}.tmp", Guid.NewGuid()));
            }
            return Path.Combine(destinationFolder, destinationFileName);
        }

        private void Download(Uri source, string fileDestination, long totalBytesToReceive)
        {
            try
            {
                FileUtils.TryGetFileSize(fileDestination, out long seekPosition);

                TryCleanupExistingDownloadWebClient();
                downloadWebClient = CreateWebClient();
                downloadWebClient.OpenReadAsync(source, seekPosition);
            }
            catch (Exception e)
            {
                if (!AttemptDownload())
                {
                    InvokeDownloadCompleted(CompletedState.Failed, localFileName, e);
                }
            }
        }

        private WebHeaderCollection GetHttpHeaders(Uri source)
        {
            try
            {
                WebRequest webRequest = WebRequest.Create(source);
                webRequest.Method = WebRequestMethods.Http.Head;

                using (WebResponse webResponse = webRequest.GetResponse())
                {
                    return webResponse.Headers;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs args)
        {
            DownloadFileProgressChangedArgs e = new DownloadFileProgressChangedArgs(args.ProgressPercentage, args.BytesReceived, args.TotalBytesToReceive);

            OnDownloadProgressChanged(sender, e);
        }

        private void OnDownloadProgressChanged(object sender, DownloadFileProgressChangedArgs args)
        {
            if (BytesReceived < args.BytesReceived)
            {
                ////bytes growing? we have connection!
                attemptNumber = 1;
            }

            BytesReceived = args.BytesReceived;
            TotalBytesToReceive = args.TotalBytesToReceive;

            DownloadProgressChanged.SafeInvoke(sender, args);
        }

        private void InvokeDownloadCompleted(CompletedState downloadCompletedState, string fileName, System.Exception error = null, bool fromCache = false)
        {
            TimeSpan downloadTime = fromCache ? TimeSpan.Zero : DateTime.Now.Subtract(DownloadStartTime);
            if (worker != null)
            {
                BytesReceived = worker.Position;
            }

            DownloadFileCompleted.SafeInvoke(this, new DownloadFileCompletedArgs(downloadCompletedState, fileName, fileSource, downloadTime, TotalBytesToReceive, BytesReceived, error));
        }

        private void OnOpenReadCompleted(object sender, OpenReadCompletedEventArgs args)
        {
            DownloadWebClient webClient = sender as DownloadWebClient;
            if (webClient == null)
            {
                return;
            }

            lock (cancelSync)
            {
                if (isCancelled)
                {
                    return;
                }

                if (!webClient.HasResponse)
                {
                    TriggerWebClientDownloadFileAsync();
                    return;
                }

                bool appendExistingChunk = webClient.IsPartialResponse;
                Stream destinationStream = CreateDestinationStream(appendExistingChunk);
                if (destinationStream != null)
                {
                    TrySetStreamReadTimeout(args.Result, (int)SourceStreamReadTimeout.TotalMilliseconds);

                    worker = new StreamCopyWorker();
                    worker.Completed += OnWorkerCompleted;
                    worker.ProgressChanged += OnWorkerProgressChanged;
                    worker.CopyAsync(args.Result, destinationStream, TotalBytesToReceive);
                }
            }
        }

        private bool TrySetStreamReadTimeout(Stream stream, int timeout)
        {
            try
            {
                stream.ReadTimeout = timeout;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Stream CreateDestinationStream(bool append)
        {
            FileStream destinationStream = null;
            try
            {
                string destinationDirectory = Path.GetDirectoryName(localFileName);
                if (destinationDirectory != null && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                destinationStream = new FileStream(localFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                if (append)
                {
                    destinationStream.Seek(0, SeekOrigin.End);
                }
                else
                {
                    destinationStream.SetLength(0);
                }
            }
            catch (Exception ex)
            {
                if (destinationStream != null)
                {
                    destinationStream.Dispose();
                    destinationStream = null;
                }
                OnDownloadCompleted(downloadWebClient, new AsyncCompletedEventArgs(ex, false, null));
            }
            return destinationStream;
        }

        private void OnWorkerProgressChanged(object sender, StreamCopyProgressEventArgs eventArgs)
        {
            if (isCancelled)
            {
                return;
            }

            if (TotalBytesToReceive == 0)
            {
                return;
            }
            long progress = eventArgs.BytesReceived / TotalBytesToReceive;
            int progressPercentage = (int)(progress * 100);

            OnDownloadProgressChanged(this, new DownloadFileProgressChangedArgs(progressPercentage, eventArgs.BytesReceived, TotalBytesToReceive));
        }

        private void OnWorkerCompleted(object sender, StreamCopyCompleteEventArgs eventArgs)
        {
            try
            {
                OnDownloadCompleted(downloadWebClient, new AsyncCompletedEventArgs(eventArgs.Exception, eventArgs.CompleteState == CompletedState.Canceled, null));
            }
            finally
            {
                worker.ProgressChanged -= OnWorkerProgressChanged;
                worker.Completed -= OnWorkerCompleted;
                worker.Dispose();
            }
        }

        /// <summary>
        /// OnDownloadCompleted event handler
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="args">AsyncCompletedEventArgs instance</param>
        protected void OnDownloadCompleted(object sender, AsyncCompletedEventArgs args)
        {
            DownloadWebClient webClient = sender as DownloadWebClient;
            if (webClient == null)
            {
                //logger.Warn("Wrong sender in OnDownloadCompleted: Actual:{0} Expected:{1}", sender.GetType(), typeof(DownloadWebClient));
                InvokeDownloadCompleted(CompletedState.Failed, localFileName);
                return;
            }

            if (args.Cancelled)
            {
                //logger.Debug("Download cancelled. Source: {0} Destination: {1}", fileSource, localFileName);
                DeleteDownloadedFile();

                InvokeDownloadCompleted(CompletedState.Canceled, localFileName);
                readyToDownload.Set();
            }
            else if (args.Error != null)
            {
                if (isFallback)
                {
                    DeleteDownloadedFile();
                }

                ////We may have NameResolutionFailure on internet connectivity problem.
                ////We don't use DnsFallbackResolver if we successfully started downloading, and then got internet problem.
                ////If we change [this.fileSource] here - we lose downloaded chunk in Cache (i.e. we create a new Cache item for new [this.fileSource]
                if (attemptNumber == 1 && DnsFallbackResolver != null && IsNameResolutionFailure(args.Error))
                {
                    Uri newFileSource = DnsFallbackResolver.Resolve(fileSource);
                    if (newFileSource != null)
                    {
                        fileSource = newFileSource;
                        AttemptDownload();
                        return;
                    }
                }

                if (!AttemptDownload())
                {
                    InvokeDownloadCompleted(CompletedState.Failed, null, args.Error);
                    readyToDownload.Set();
                }
            }
            else
            {
                if (useFileNameFromServer)
                {
                    localFileName = ApplyNewFileName(localFileName, webClient.GetOriginalFileNameFromDownload());
                }

                if (UseCaching)
                {
                    downloadCache.Add(fileSource, localFileName, webClient.ResponseHeaders);
                }

                ////we may have the destination file not immediately closed after downloading
                WaitFileClosed(localFileName, TimeSpan.FromSeconds(3));

                InvokeDownloadCompleted(CompletedState.Succeeded, localFileName, null);
                readyToDownload.Set();
            }
        }

        /// <summary>
        /// Rename oldFilePath to newFileName , placing file in same folder or in temporary folder if renaming failed. 
        /// </summary>
        /// <param name="oldFilePath">Full path and name of the file to be renamed</param>
        /// <param name="newFileName">New file name</param>
        /// <returns>Full path to renamed file</returns>
        protected virtual string ApplyNewFileName(string oldFilePath, string newFileName)
        {
            string downloadedFileName = Path.GetFileName(oldFilePath);
            string downloadDirectory = Path.GetDirectoryName(oldFilePath);

            if (newFileName == null || newFileName == downloadedFileName || downloadDirectory == null)
            {
                return oldFilePath;
            }

            string newFilePath = Path.Combine(downloadDirectory, newFileName);

            if (File.Exists(newFilePath))
            {
                try
                {
                    File.Delete(newFilePath);
                }
                catch (Exception)
                {
                    newFilePath = Path.Combine(CreateTempFolder(downloadDirectory), newFileName);
                }
            }

            if (newFilePath == oldFilePath)
            {
                return oldFilePath;
            }

            File.Move(oldFilePath, newFilePath);
            return newFilePath;
        }

        private void TriggerDownloadWebClientCancelAsync()
        {
            if (downloadWebClient != null)
            {
                downloadWebClient.CancelAsync();
                downloadWebClient.OpenReadCompleted -= OnOpenReadCompleted;
            }
        }

        private string CreateTempFolder(string rootFolderPath)
        {
            while (true)
            {
                string folderPath = Path.Combine(rootFolderPath, Path.GetRandomFileName());
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    return folderPath;
                }
            }
        }

        private bool IsNameResolutionFailure(System.Exception exception)
        {
            WebException webException = exception as WebException;
            return webException != null && webException.Status == WebExceptionStatus.NameResolutionFailure;
        }

        private bool WaitSafeStart()
        {
            if (!readyToDownload.WaitOne(SafeWaitTimeout))
            {
                return false;
            }
            readyToDownload.Reset();
            return true;
        }

        private void WaitFileClosed(string fileName, TimeSpan waitTimeout)
        {
            TimeSpan waitCounter = TimeSpan.Zero;
            while (waitCounter < waitTimeout)
            {
                try
                {
                    FileStream fileHandle = File.Open(fileName, FileMode.Open, FileAccess.Read);
                    fileHandle.Dispose();
                    Thread.Sleep(500);
                    return;
                }
                catch (System.Exception)
                {
                    waitCounter = waitCounter.Add(TimeSpan.FromMilliseconds(500));
                    Thread.Sleep(500);
                }
            }
        }

        private bool IsCancelled()
        {
            lock (cancelSync)
            {
                if (isCancelled)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Do the actual dispose
        /// </summary>
        /// <param name="disposing">True if called from Dispose</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (readyToDownload.WaitOne(TimeSpan.FromMinutes(10)))
                    {
                        if (worker != null)
                        {
                            worker.Dispose();
                        }
                        if (downloadWebClient != null)
                        {
                            downloadWebClient.Dispose();
                        }
                        readyToDownload.Close();
                        attemptTimer.Dispose();
                    }
                }
                disposed = true;
            }
        }
    }
#nullable enable
}