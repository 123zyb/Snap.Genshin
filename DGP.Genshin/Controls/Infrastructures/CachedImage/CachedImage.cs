﻿using Nito.AsyncEx;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DGP.Genshin.Controls.Infrastructures.CachedImage
{
    /// <summary>
    /// <see cref="System.Windows.Controls.Image"/> 的带缓存包装
    /// 实现本地图像缓存
    /// </summary>
    /// https://github.com/floydpink/CachedImage/blob/main/source/Image.cs
    public class CachedImage : System.Windows.Controls.Image
    {
        static CachedImage()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CachedImage),
                new FrameworkPropertyMetadata(typeof(CachedImage)));
        }

        public string ImageUrl
        {
            get => (string)GetValue(ImageUrlProperty);
            set => SetValue(ImageUrlProperty, value);
        }

        public BitmapCreateOptions CreateOptions
        {
            get => (BitmapCreateOptions)GetValue(CreateOptionsProperty);
            set => SetValue(CreateOptionsProperty, value);
        }
        private static readonly AsyncLock _mutex = new AsyncLock();
        private static async void ImageUrlPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            using (await _mutex.LockAsync())
            {
                string? url = e.NewValue as string;

                if (string.IsNullOrEmpty(url))
                {
                    return;
                }

                CachedImage cachedImage = (CachedImage)obj;
                BitmapImage bitmapImage = new BitmapImage();

                try
                {
                    MemoryStream? memoryStream = await FileCache.HitAsync(url);
                    if (memoryStream == null)
                    {
                        cachedImage.Source = null;
                        return;
                    }
                    bitmapImage.BeginInit();
                    bitmapImage.CreateOptions = cachedImage.CreateOptions;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    cachedImage.Source = bitmapImage;
                }
                catch (Exception)
                {
                    // ignored, in case the downloaded file is a broken or not an image.
                }
            }
        }

        public static readonly DependencyProperty ImageUrlProperty =
            DependencyProperty.Register("ImageUrl", typeof(string), typeof(CachedImage), new PropertyMetadata("", ImageUrlPropertyChanged));

        public static readonly DependencyProperty CreateOptionsProperty =
            DependencyProperty.Register("CreateOptions", typeof(BitmapCreateOptions), typeof(CachedImage));
    }
}