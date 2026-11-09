using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PictureChecklist
{
    // Local library entries only. Never changes a checklist, its images or progress.
    // Kept apart from preferences so older windows cannot overwrite removal records.
    internal static class LibraryVisibility
    {
        static string FileName { get { return Path.Combine(Preferences.Profile, "library-visibility.json"); } }
        internal static string Key(string folder)
        {
            return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }
        static Dictionary<string, bool> Read(bool strict)
        {
            if (!File.Exists(FileName)) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try {
                var data = ModelJson.Read<Dictionary<string, bool>>(File.ReadAllText(FileName, Encoding.UTF8));
                if (data == null) throw new InvalidDataException("清单库记录无效。");
                return new Dictionary<string, bool>(data, StringComparer.OrdinalIgnoreCase);
            } catch (Exception ex) {
                if (strict) throw new IOException("无法读取清单库记录，请稍后重试。原清单资料未修改。", ex);
                try {
                    var backup = ModelJson.Read<Dictionary<string, bool>>(File.ReadAllText(FileName + ".bak", Encoding.UTF8));
                    if (backup != null) return new Dictionary<string, bool>(backup, StringComparer.OrdinalIgnoreCase);
                } catch { }
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }
        internal static HashSet<string> HiddenFolders()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Read(false)) if (pair.Value) result.Add(pair.Key);
            return result;
        }
        internal static bool IsHidden(string folder)
        {
            return !String.IsNullOrWhiteSpace(folder) && HiddenFolders().Contains(Key(folder));
        }
        internal static void SetHidden(string folder, bool hidden)
        {
            string key = Key(folder);
            Directory.CreateDirectory(Preferences.Profile);
            using (var mutex = new Mutex(false, @"Local\PictureChecklistLibraryVisibilityV1")) {
                bool held = false;
                string temporary = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    try { held = mutex.WaitOne(1500); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("清单库正在更新，请稍后重试。");
                    var data = Read(true);
                    bool previous;
                    bool wasHidden = data.TryGetValue(key, out previous) && previous;
                    if (wasHidden == hidden) return;
                    if (hidden) data[key] = true; else data.Remove(key);
                    File.WriteAllText(temporary, ModelJson.Write(data), new UTF8Encoding(false));
                    if (File.Exists(FileName)) File.Replace(temporary, FileName, FileName + ".bak", true);
                    else File.Move(temporary, FileName);
                } finally {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                    if (held) mutex.ReleaseMutex();
                }
            }
        }
    }
}
