﻿using DGP.Genshin.Helper;
using ModernWpf.Controls;
using Snap.Win32;
using System.Threading.Tasks;
using System.Windows;

namespace DGP.Genshin.Control.Cookie
{
    public partial class CookieDialog : ContentDialog
    {
        private const string cookieCode = "javascript:(()=>{_=(n)=>{for(i in(r=document.cookie.split(';'))){var a=r[i].split('=');if(a[0].trim()==n)return a[1]}};c=_('account_id')||alert('无效的Cookie,请重新登录!');c&&confirm('将Cookie复制到剪贴板?')&&copy(document.cookie)})();";

        public CookieDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 获取输入的Cookie
        /// </summary>
        public async Task<(ContentDialogResult result, string cookie)> GetInputCookieAsync()
        {
            ContentDialogResult result = await ShowAsync();
            return (result, InputText.Text);
        }

        private void AutoCookieButtonClick(object sender, RoutedEventArgs e)
        {
            if (!WebView2Helper.IsSupported)
            {
                System.Windows.Controls.Button button = (System.Windows.Controls.Button)sender;
                button.IsEnabled = false;
                button.Content = "需要先安装 WebView2运行时";

                new WebView2RuntimeWindow().ShowDialog();
            }
            else
            {
                using (CookieWindow cookieWindow = new())
                {
                    cookieWindow.ShowDialog();
                    bool isLoggedIn = cookieWindow.IsLoggedIn;
                    if (isLoggedIn)
                    {
                        InputText.Text = cookieWindow.Cookie;
                    }
                }
            }
        }

        private void InputText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string text = InputText.Text;
            bool inputEmpty = string.IsNullOrEmpty(text);
            bool inputHasAccountId = text.Contains("account_id");

            (PrimaryButtonText, IsPrimaryButtonEnabled) = (inputEmpty, inputHasAccountId) switch
            {
                (true, _) => ("请输入Cookie", false),
                (false, true) => ("确认", true),
                (false, false) => ("该Cookie无效", false)
            };
        }

        private void CopyCodeButtonClick(object sender, RoutedEventArgs e)
        {
            //clear before copy
            Clipboard.Clear();
            Clipboard2.SetText(cookieCode);
        }
    }
}
