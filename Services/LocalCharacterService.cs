using System;
using System.IO;
using System.Collections.Generic;
using SaintsRowReborn.Hydra.DataTypes;

namespace SaintsRowReborn.Services;

public static class LocalCharacterService
{
    public static string ResolveCharactersDirectory()
    {
        string baseDir = AppContext.BaseDirectory;

        if (baseDir.Contains("/bin/Debug") || baseDir.Contains("/bin/Release"))
        {
            int binIndex = baseDir.IndexOf("/bin/");
            string projectDir = baseDir.Substring(0, binIndex);
            
            return Path.Combine(projectDir, "Characters");
        }

        if (baseDir.Contains(".app/Contents/"))
        {
            int appIndex = baseDir.IndexOf(".app");
            string appBundlePath = baseDir.Substring(0, appIndex + 4);
            string parentDir = Directory.GetParent(appBundlePath)?.FullName ?? baseDir;
            return Path.Combine(parentDir, "Characters");
        }

        return Path.Combine(baseDir, "Characters");
    }

    public static HydraArray GetDownloadedCharacters()
    {
        var arr = new HydraArray();
        string dir = ResolveCharactersDirectory();
        
        if (!Directory.Exists(dir)) return arr;

        foreach (var file in Directory.GetFiles(dir, "*.sr_character"))
        {
            try
            {
                byte[] payloadBytes = File.ReadAllBytes(file);
                
                using var ms = new MemoryStream();
                ms.WriteByte((byte)new HydraHashMap().DataType);
                ms.Write(payloadBytes, 0, payloadBytes.Length);
                
                var parsed = HydraItemDeserializer.DeserializeAll(ms.ToArray());
                if (parsed.Count > 0 && parsed[0] is HydraHashMap metaMap)
                {
                    long charId = 0;
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    long.TryParse(fileName, out charId);
                    
                    var outerMap = new HydraHashMap(new Dictionary<string, IHydraItem>());
                    
                    outerMap.Items["guid"] = new HydraInt64(charId);
                    outerMap.Items["id"] = new HydraInt64(charId);
                    outerMap.Items["owner_guid"] = new HydraInt64(1);
                    outerMap.Items["creator_guid"] = new HydraInt64(1);
                    
                    var safeMetaMap = new HydraHashMap(new Dictionary<string, IHydraItem>());
                    
                    if (metaMap.Items.ContainsKey("Name"))
                        safeMetaMap.Items["Name"] = metaMap.Items["Name"];
                    else
                        safeMetaMap.Items["Name"] = new HydraUtf8String("Unknown");
                        
                    if (metaMap.Items.ContainsKey("Avatar"))
                        safeMetaMap.Items["Avatar"] = metaMap.Items["Avatar"];
                        
                    if (metaMap.Items.ContainsKey("Avatar Version"))
                        safeMetaMap.Items["Avatar Version"] = metaMap.Items["Avatar Version"];
                        
                    outerMap.Items["meta_data"] = safeMetaMap;
                    
                    arr.Items.Add(outerMap);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UGC] Failed to load local character {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        
        return arr;
    }
}