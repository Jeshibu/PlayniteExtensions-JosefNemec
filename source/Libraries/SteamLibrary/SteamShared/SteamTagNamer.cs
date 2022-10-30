﻿using Newtonsoft.Json;
using Playnite.Common.Web;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SteamLibrary.SteamShared
{
    public class SteamTagNamer
    {
        private readonly string pluginUserDataPath;
        private readonly UniversalSteamSettings settings;
        private readonly IDownloader downloader;
        private ILogger logger = LogManager.GetLogger();

        public SteamTagNamer(string pluginUserDataPath, UniversalSteamSettings settings, IDownloader downloader)
        {
            this.pluginUserDataPath = pluginUserDataPath;
            this.settings = settings;
            this.downloader = downloader;
        }

        private string GetTagNameFilePath()
        {
            return $@"{pluginUserDataPath}\TagLocalization\{settings?.LanguageKey}.json";
        }

        private string GetPackedTagNameFilePath()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return $@"{dir}\SteamShared\TagLocalization\{settings?.LanguageKey}.json";
        }

        public Dictionary<int, string> GetTagNames()
        {
            string fileName = GetTagNameFilePath();
            if (!File.Exists(fileName))
            {
                logger.Trace($"No new tag names found for '{settings?.LanguageKey}'");
                fileName = GetPackedTagNameFilePath();
            }

            if (!File.Exists(fileName))
            {
                logger.Warn($"No tag names found for language '{settings?.LanguageKey}'");
                return new Dictionary<int, string>();
            }

            string fileContent = File.ReadAllText(fileName);
            var tagNames = JsonConvert.DeserializeObject<Dictionary<int, string>>(fileContent);
            return tagNames;
        }

        private static Regex tagNameRegex = new Regex(@"data-param=""tags"" data-value=""(?<id>[0-9]+)"" data-loc=""(?<name>.+?)""", RegexOptions.Compiled);

        public Dictionary<int, string> UpdateAndGetTagNames()
        {
            string url = $"https://store.steampowered.com/search/?l={settings?.LanguageKey}";
            var content = downloader.DownloadString(url);
            var matches = tagNameRegex.Matches(content);

            var output = new Dictionary<int, string>();
            foreach (var match in matches.Cast<Match>())
            {
                int id = int.Parse(match.Groups["id"].Value);
                if (!output.ContainsKey(id))
                {
                    string name = match.Groups["name"].Value;
                    output.Add(id, name);
                }
            }

            string serialized = JsonConvert.SerializeObject(output);
            File.WriteAllText(GetTagNameFilePath(), serialized, Encoding.Unicode);

            return output;
        }
        public string GetFinalTagName(string tagName)
        {
            tagName = HttpUtility.HtmlDecode(tagName).Trim();
            string computedTagName = settings.UseTagPrefix ? $"{settings.TagPrefix}{tagName}" : tagName;
            return computedTagName;
        }
    }
}
