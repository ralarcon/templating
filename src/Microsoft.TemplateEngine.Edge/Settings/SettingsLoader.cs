﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Utils;
using Newtonsoft.Json.Linq;

namespace Microsoft.TemplateEngine.Edge.Settings
{
    public static class SettingsLoader
    {
        private const int MaxLoadAttempts = 20;
        public static readonly string HostTemplateFileConfigBaseName = ".host.json";

        private static SettingsStore _userSettings;
        private static TemplateCache _userTemplateCache;
        private static IMountPointManager _mountPointManager;
        private static IComponentManager _componentManager;
        private static bool _isLoaded;
        private static Dictionary<Guid, MountPointInfo> _mountPoints;
        private static bool _templatesLoaded;

        public static void Save(this SettingsStore store)
        {
            JObject serialized = JObject.FromObject(store);
            Paths.User.SettingsFile.WriteAllText(serialized.ToString());
        }

        private static void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            string userSettings = null;
            using (Timing.Over("Read settings"))
                for (int i = 0; i < MaxLoadAttempts; ++i)
                {
                    try
                    {
                        userSettings = Paths.User.SettingsFile.ReadAllText("{}");
                        break;
                    }
                    catch (IOException)
                    {
                        if(i == MaxLoadAttempts - 1)
                        {
                            throw;
                        }

                        Thread.Sleep(2);
                    }
                }
            JObject parsed;
            using (Timing.Over("Parse settings"))
                parsed = JObject.Parse(userSettings);
            using (Timing.Over("Deserialize user settings"))
                _userSettings = new SettingsStore(parsed);

            using (Timing.Over("Init probing paths"))
                if (_userSettings.ProbingPaths.Count == 0)
                {
                    _userSettings.ProbingPaths.Add(Paths.User.Content);
                }

            using (Timing.Over("Init Component manager"))
                _componentManager = new ComponentManager(_userSettings);
            using (Timing.Over("Init Mount Point manager"))
                _mountPointManager = new MountPointManager(_componentManager);

            using (Timing.Over("Demand template load"))
                EnsureTemplatesLoaded();

            _mountPoints = new Dictionary<Guid, MountPointInfo>();

            using (Timing.Over("Load mount points"))
                foreach (MountPointInfo info in _userSettings.MountPoints)
                {
                    _mountPoints[info.MountPointId] = info;
                }

            _isLoaded = true;
        }

        // Loads from the template cache
        private static void EnsureTemplatesLoaded()
        {
            if (_templatesLoaded)
            {
                return;
            }

            string userTemplateCache;

            if (Paths.User.CurrentLocaleTemplateCacheFile.Exists())
            {
                using (Timing.Over("Read template cache"))
                    userTemplateCache = Paths.User.CurrentLocaleTemplateCacheFile.ReadAllText("{}");
            }
            else if (Paths.User.CultureNeutralTemplateCacheFile.Exists())
            {
                // clone the culture neutral cache
                // this should not occur if there are any langpacks installed for this culture.
                // when they got installed, the cache should have been created for that locale.
                using (Timing.Over("Clone cultural neutral cache"))
                {
                    userTemplateCache = Paths.User.CultureNeutralTemplateCacheFile.ReadAllText("{}");
                    Paths.User.CurrentLocaleTemplateCacheFile.WriteAllText(userTemplateCache);
                }
            }
            else
            {
                userTemplateCache = "{}";
            }

            JObject parsed;
            using (Timing.Over("Parse template cache"))
                parsed = JObject.Parse(userTemplateCache);
            using (Timing.Over("Init template cache"))
                _userTemplateCache = new TemplateCache(parsed);

            _templatesLoaded = true;
        }

        public static void Reload()
        {
            _isLoaded = false;
            EnsureLoaded();
        }

        private static void UpdateTemplateListFromCache(TemplateCache cache, ISet<ITemplateInfo> templates)
        {
            using (Timing.Over("Enumerate infos"))
                templates.UnionWith(cache.TemplateInfo);
        }

        public static ITemplate LoadTemplate(ITemplateInfo info)
        {
            IGenerator generator;
            if (!Components.TryGetComponent(info.GeneratorId, out generator))
            {
                return null;
            }

            IMountPoint mountPoint;
            if (!_mountPointManager.TryDemandMountPoint(info.ConfigMountPointId, out mountPoint))
            {
                return null;
            }
            IFileSystemInfo config = mountPoint.FileSystemInfo(info.ConfigPlace);

            IFileSystemInfo localeConfig = null;
            if (!string.IsNullOrEmpty(info.LocaleConfigPlace)
                    && info.LocaleConfigMountPointId != null
                    && info.LocaleConfigMountPointId != Guid.Empty)
            {
                IMountPoint localeMountPoint;
                if (!_mountPointManager.TryDemandMountPoint(info.LocaleConfigMountPointId, out localeMountPoint))
                {
                    // TODO: decide if we should proceed without loc info, instead of bailing.
                    return null;
                }

                localeConfig = localeMountPoint.FileSystemInfo(info.LocaleConfigPlace);
            }

            IFile hostTemplateConfigFile = null;
            if (!string.IsNullOrEmpty(EngineEnvironmentSettings.Host.HostIdentifier))
            {
                string hostTemplateFileName = string.Join(string.Empty, EngineEnvironmentSettings.Host.HostIdentifier, HostTemplateFileConfigBaseName);
                hostTemplateConfigFile = config.Parent.EnumerateFiles(hostTemplateFileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
            }


            ITemplate template;
            using (Timing.Over("Template from config"))
                if (generator.TryGetTemplateFromConfigInfo(config, out template, localeConfig, hostTemplateConfigFile))
                {
                    return template;
                }
                else
                {
                    //TODO: Log the failure to read the template info
                }

            return null;
        }

        public static IComponentManager Components
        {
            get
            {
                EnsureLoaded();
                return _componentManager;
            }
        }

        public static IEnumerable<MountPointInfo> MountPoints
        {
            get
            {
                EnsureLoaded();
                return _mountPoints.Values;
            }
        }

        public static void GetTemplates(HashSet<ITemplateInfo> templates)
        {
            using (Timing.Over("Settings init"))
                EnsureLoaded();
            using (Timing.Over("Template load"))
                UpdateTemplateListFromCache(_userTemplateCache, templates);
        }

        public static void WriteTemplateCache(IList<TemplateInfo> templates, string locale, bool isCurrentCache)
        {
            TemplateCache cache = new TemplateCache();
            cache.TemplateInfo.AddRange(templates);
            JObject serialized = JObject.FromObject(cache);
            Paths.User.ExplicitLocaleTemplateCacheFile(locale).WriteAllText(serialized.ToString());

            if (isCurrentCache)
            {
                _userTemplateCache = cache;
            }
        }

        public static void AddProbingPath(string probeIn)
        {
            const int maxAttempts = 10;
            int attemptCount = 0;
            bool successfulWrite = false;

            EnsureLoaded();
            while (!successfulWrite && attemptCount++ < maxAttempts)
            {
                if (!_userSettings.ProbingPaths.Add(probeIn))
                {
                    return;
                }

                try
                {
                    _userSettings.Save();
                    successfulWrite = true;
                }
                catch
                {
                    Thread.Sleep(10);
                    Reload();
                }
            }
        }

        public static bool TryGetMountPointInfo(Guid mountPointId, out MountPointInfo info)
        {
            EnsureLoaded();
            using(Timing.Over("Mount point lookup"))
            return _mountPoints.TryGetValue(mountPointId, out info);
        }

        public static bool TryGetMountPointInfoFromPlace(string mountPointPlace, out MountPointInfo info)
        {
            EnsureLoaded();
            using (Timing.Over("Mount point place lookup"))
                foreach (MountPointInfo mountInfoToCheck in _mountPoints.Values)
                {
                    if (mountPointPlace.Equals(mountInfoToCheck.Place, StringComparison.OrdinalIgnoreCase))
                    {
                        info = mountInfoToCheck;
                        return true;
                    }
                }

            info = null;
            return false;
        }

        public static bool TryGetMountPointFromPlace(string mountPointPlace, out IMountPoint mountPoint)
        {
            if (! TryGetMountPointInfoFromPlace(mountPointPlace, out MountPointInfo info))
            {
                mountPoint = null;
                return false;
            }

            return _mountPointManager.TryDemandMountPoint(info.MountPointId, out mountPoint);
        }

        public static void AddMountPoint(IMountPoint mountPoint)
        {
            if(_mountPoints.Values.Any(x => string.Equals(x.Place, mountPoint.Info.Place) && x.ParentMountPointId == mountPoint.Info.ParentMountPointId))
            {
                return;
            }

            _mountPoints[mountPoint.Info.MountPointId] = mountPoint.Info;
            _userSettings.MountPoints.Add(mountPoint.Info);
            JObject serialized = JObject.FromObject(_userSettings);
            Paths.User.SettingsFile.WriteAllText(serialized.ToString());
        }

        public static bool TryGetFileFromIdAndPath(Guid mountPointId, string place, out IFile file)
        {
            if (!string.IsNullOrEmpty(place) && _mountPointManager.TryDemandMountPoint(mountPointId, out IMountPoint mountPoint))
            {
                file = mountPoint.FileInfo(place);
                return file != null && file.Exists;
            }

            file = null;
            return false;
        }
    }
}
