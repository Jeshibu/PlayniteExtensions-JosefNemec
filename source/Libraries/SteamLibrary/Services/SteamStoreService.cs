using AngleSharp.Parser.Html;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Events;
using SteamLibrary.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamLibrary.Services
{
    public class SteamStoreService
    {
        private const string recommendationQueueUrl = "https://store.steampowered.com/explore/";
        private readonly ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI PlayniteApi { get; }
        private SteamUserToken? userTokenFromLogin;

        public SteamStoreService(IPlayniteAPI playniteApi)
        {
            PlayniteApi = playniteApi;
        }

        public async Task<SteamUserDataRoot> GetUserDataAsync()
        {
            var str = await DownloadPageSourceAsync("https://store.steampowered.com/dynamicstore/userdata/");

            if (str.Trim().StartsWith("<html", StringComparison.InvariantCultureIgnoreCase)
                && str.Contains("<body", StringComparison.InvariantCultureIgnoreCase))
            {
                var doc = await new HtmlParser().ParseAsync(str);
                str = doc.GetElementsByTagName("body").FirstOrDefault()?.TextContent;
            }

            var model = JsonConvert.DeserializeObject<SteamUserDataRoot>(str);
            return model;
        }

        private async Task<string> DownloadPageSourceAsync(string url)
        {
            using var webView = PlayniteApi.WebViews.CreateOffscreenView();
            webView.NavigateAndWait(url);
            return await webView.GetPageSourceAsync();
        }

        private async Task<SteamUserToken?> GetSteamUserTokenFromWebViewAsync(IWebView webView)
        {
            logger.Info("GetSteamUserTokenFromWebViewAsync");
            var url = webView.GetCurrentAddress();
            if (url.Contains("/login"))
            {
                logger.Info($"Current URL contains /login, canceling: {url}");
                return null;
            }

            var source = await webView.GetPageSourceAsync();
            await GenerateLog(source);

            var userIdMatch = Regex.Match(source, "&quot;steamid&quot;:&quot;(?<id>[0-9]+)&quot;");
            var tokenMatch = Regex.Match(source, "&quot;webapi_token&quot;:&quot;(?<token>[^&]+)&quot;");

            if (!userIdMatch.Success || !tokenMatch.Success)
            {
                if (!userIdMatch.Success)
                    logger.Warn("Could not find Steam user ID");

                if (!tokenMatch.Success)
                    logger.Warn("Could not find Steam token");

                return null;
            }

            var token = new SteamUserToken(
                ulong.Parse(userIdMatch.Groups["id"].Value),
                tokenMatch.Groups["token"].Value);

            logger.Info($"Returning Steam user ID: {token.UserId}");
            return token;
        }

        private async Task GenerateLog(string pageSource)
        {
            var doc = await new HtmlParser().ParseAsync(pageSource);
            var configElement = doc.GetElementById("application_config");
            if (configElement == null)
            {
                logger.Warn("Could not find application config element");
                return;
            }

            logger.Info(configElement.OuterHtml);

            LogAttribute<StoreUserConfig>("data-store_user_config");
            LogAttribute<UserInfo>("data-userinfo");
            return;

            void LogAttribute<T>(string attributeName)
            {
                if (!configElement.HasAttribute(attributeName))
                {
                    logger.Warn($"Could not find config attribute: {attributeName}");
                    return;
                }

                var attrValue = configElement.GetAttribute(attributeName);
                try
                {
                    var obj = JsonConvert.DeserializeObject<T>(attrValue);
                    logger.Info($"{typeof(T).Name}: {JsonConvert.SerializeObject(obj)}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Config attribute parsing error for {attributeName}");
                }
            }
        }

        public async Task<SteamUserToken> GetAccessTokenAsync()
        {
            using var view = PlayniteApi.WebViews.CreateOffscreenView();

            view.NavigateAndWait(recommendationQueueUrl);

            return await GetSteamUserTokenFromWebViewAsync(view)
                   ?? throw new Exception(PlayniteApi.Resources.GetString(LOC.SteamNotLoggedInError));
        }

        public SteamUserToken? Login()
        {
            var view = PlayniteApi.WebViews.CreateView(600, 720);
            try
            {
                view.LoadingChanged += CloseWhenLoggedIn;
                view.DeleteDomainCookies(".steamcommunity.com");
                view.DeleteDomainCookies("steamcommunity.com");
                view.DeleteDomainCookies("steampowered.com");
                view.DeleteDomainCookies("store.steampowered.com");
                view.DeleteDomainCookies("help.steampowered.com");
                view.DeleteDomainCookies("login.steampowered.com");
                view.Navigate(recommendationQueueUrl);

                userTokenFromLogin = null;
                view.OpenDialog();
                return userTokenFromLogin;
            }
            catch (Exception e) when (!Debugger.IsAttached)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(PlayniteApi.Resources.GetString(LOC.SteamNotLoggedInError), "");
                logger.Error(e, "Failed to authenticate user.");
                return null;
            }
            finally
            {
                if (view != null)
                {
                    view.LoadingChanged -= CloseWhenLoggedIn;
                    view.Dispose();
                }
            }
        }

        private async void CloseWhenLoggedIn(object sender, WebViewLoadingChangedEventArgs e)
        {
            try
            {
                if (e.IsLoading)
                    return;

                var view = (IWebView)sender;
                var token = await GetSteamUserTokenFromWebViewAsync(view);
                if (token?.AccessToken != null)
                {
                    userTokenFromLogin = token;
                    view.Close();
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to check authentication status");
            }
        }
    }
}