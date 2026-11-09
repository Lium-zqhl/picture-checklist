using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace PictureChecklist
{
    public static class Store
    {
        public const string ProjectFile = "checklist.json";
        internal const string Previous = "backups/previous.json";
        internal const int MaxJson = 32 * 1024 * 1024;
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static readonly string[] MigrationFiles = { "migration/tasks.json", "migration/progress.json", "migration/report.json" };
        private sealed class Revision { public string Hash; }
        private static readonly ConditionalWeakTable<Document, Revision> Revisions = new ConditionalWeakTable<Document, Revision>();

        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = MaxJson, RecursionLimit = 80 }; }

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        internal static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static string HashFile(string filename)
        {
            using (var file = File.OpenRead(filename)) return Hash(file);
        }

        internal static string ReadText(string filename)
        {
            Require(new FileInfo(filename).Length <= MaxJson, "JSON 文件过大：" + Path.GetFileName(filename));
            return File.ReadAllText(filename, Utf8);
        }

        internal static Dictionary<string,object> Object(object value, string context)
        {
            var map = value as Dictionary<string,object>;
            Require(map != null, context + " 应为 JSON 对象。");
            return map;
        }

        internal static object[] ArrayValue(object value, string context)
        {
            var arr = value as object[];
            Require(arr != null, context + " 应为 JSON 数组。");
            return arr;
        }

        internal static void Keys(Dictionary<string,object> map, params string[] names)
        {
            Require(map.Count == names.Length && names.All(map.ContainsKey), "字段缺失或版本无法识别：" + String.Join("、", map.Keys));
        }

        internal static void SafeRelative(string relative)
        {
            Require(!String.IsNullOrEmpty(relative) && relative.Length <= 1024, "相对路径为空或过长。");
            foreach (var part in relative.Split('/'))
            {
                Require(part.Length > 0 && part.Length <= 255 && !part.EndsWith(".") && !part.EndsWith(" ")
                    && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    && !Regex.IsMatch(part, @"^(con|prn|aux|nul|com[0-9¹²³]|lpt[0-9¹²³]|conin\$|conout\$)(\.|$)", RegexOptions.IgnoreCase),
                    "不安全的相对路径：" + relative);
            }
        }

        internal static void NoLink(string path)
        {
            try { Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "项目路径中包含链接：" + path); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        internal static string PathIn(string folder, string relative)
        {
            SafeRelative(relative);
            string path = Path.GetFullPath(folder);
            NoLink(path);
            foreach (var part in relative.Split('/'))
            {
                path = Path.Combine(path, part);
                NoLink(path);
            }
            return path;
        }

        internal static void Outside(string source, string target)
        {
            string a = Path.GetFullPath(source).TrimEnd('\\','/');
            string b = Path.GetFullPath(target).TrimEnd('\\','/');
            Require(!String.Equals(a, b, StringComparison.OrdinalIgnoreCase) &&
                !b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "目标必须位于源项目之外。");
        }

        // Build beside the destination and publish only the fully verified project.
        internal static Document Stage(string target, Func<string,Document> build)
        {
            string full = Path.GetFullPath(target).TrimEnd('\\','/');
            NoLink(full);
            if (Directory.Exists(full)) Require(!Directory.EnumerateFileSystemEntries(full).Any(), "目标文件夹必须为空。");
            Require(!File.Exists(full), "目标路径已被文件占用。");
            string parent = Path.GetDirectoryName(full);
            Require(!String.IsNullOrEmpty(parent), "请选择项目子目录。");
            Directory.CreateDirectory(parent);
            string stage = Path.Combine(parent, ".checklist-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            bool removedEmpty = false;
            try
            {
                Document result = build(stage);
                if (Directory.Exists(full))
                {
                    NoLink(full);
                    Directory.Delete(full, false);
                    removedEmpty = true;
                }
                Directory.Move(stage, full);
                return result;
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                if (removedEmpty && !Directory.Exists(full) && !File.Exists(full)) Directory.CreateDirectory(full);
            }
        }

        internal static void WriteNew(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(data, 0, data.Length); file.Flush(true); }
        }

        internal static void Copy(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, false);
            // A read-only original may still be imported into a writable project.
            File.SetAttributes(target, FileAttributes.Normal);
        }

        internal static void PublishFile(string temp, string target, string backup)
        {
            if (File.Exists(target)) File.Replace(temp, target, backup, true);
            else File.Move(temp, target);
        }

        private static void CheckShape(string json)
        {
            var d = Object(Json().DeserializeObject(json), "项目");
            Keys(d, "Version", "Id", "Name", "Updated", "Archived", "Items", "Assets");
            Require(d["Version"] is int && (int)d["Version"] == 1 && d["Archived"] is bool && d["Id"] is string && d["Name"] is string && d["Updated"] is string,
                "无法识别的项目版本或字段类型。");
            foreach (object value in ArrayValue(d["Items"], "条目"))
            {
                var e = Object(value, "条目");
                Keys(e, "Id", "Title", "Body", "Caution", "Note", "Status", "Updated", "NeedsConfirmation", "Clarified", "Deleted", "LegacyNumber", "LegacyActions", "AssetIds");
                foreach (var k in new[] {"Id","Title","Body","Caution","Note","Status","Updated"}) Require(e[k] is string, "条目文本字段无效：" + k);
                foreach (var k in new[] {"NeedsConfirmation","Clarified","Deleted"}) Require(e[k] is bool, "条目标记无效：" + k);
                Require(e["LegacyNumber"] is int && ArrayValue(e["LegacyActions"], "旧动作").All(x => x is string)
                    && ArrayValue(e["AssetIds"], "图片编号").All(x => x is string), "条目旧编号、动作或图片编号无效。");
            }
            foreach (object value in ArrayValue(d["Assets"], "素材"))
            {
                var a = Object(value, "素材"); Keys(a, "Id", "RelativePath", "OriginalName", "Sha256");
                Require(a.Values.All(v => v is string), "素材字段类型无效。");
            }
        }

        public static string AssetPath(string folder, Asset asset)
        {
            Require(asset != null && asset.RelativePath != null && asset.RelativePath.StartsWith("assets/", StringComparison.Ordinal)
                && asset.RelativePath.Split('/').Length == 2, "素材必须在 assets 子目录中。");
            return PathIn(folder, asset.RelativePath);
        }

        internal static void Validate(string folder, Document doc, bool checkHashes)
        {
            Require(doc != null && doc.Version == 1 && !String.IsNullOrWhiteSpace(doc.Id)
                && !String.IsNullOrWhiteSpace(doc.Name) && doc.Updated != null && doc.Items != null && doc.Assets != null, "项目基本字段无效。");
            var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Asset a in doc.Assets)
            {
                Require(a != null && !String.IsNullOrWhiteSpace(a.Id) && assetIds.Add(a.Id) && !String.IsNullOrWhiteSpace(a.OriginalName)
                    && a.Sha256 != null && Regex.IsMatch(a.Sha256, @"\A[0-9a-f]{64}\z") && paths.Add(a.RelativePath), "素材编号、路径、名称或哈希无效。");
                string path = AssetPath(folder, a);
                Require(File.Exists(path), "缺少原图：" + a.OriginalName);
                if (checkHashes) Require(HashFile(path) == a.Sha256, "原图校验失败：" + a.OriginalName);
            }
            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedAssets = new HashSet<string>(doc.Assets.Select(a => a.Id), StringComparer.Ordinal);
            foreach (Entry e in doc.Items)
            {
                Require(e != null && !String.IsNullOrWhiteSpace(e.Id) && itemIds.Add(e.Id)
                    && (e.Status == "todo" || e.Status == "doing" || e.Status == "done")
                    && e.Title != null && e.Body != null && e.Note != null && e.Caution != null && e.Updated != null
                    && e.LegacyNumber >= 0 && e.LegacyActions != null && e.LegacyActions.All(x => x != null) && e.AssetIds != null, "条目字段、状态或编号无效。");
                var refs = new HashSet<string>(StringComparer.Ordinal);
                Require(e.AssetIds.All(id => id != null && referencedAssets.Contains(id) && refs.Add(id)), "条目引用的图片不存在或重复：" + e.Title);
            }
        }

        internal static Document ReadDocument(string folder, string relative)
        {
            string filename = PathIn(folder, relative);
            Require(new FileInfo(filename).Length <= MaxJson, "项目文件过大。");
            byte[] raw = File.ReadAllBytes(filename);
            string json;
            using (var reader = new StreamReader(new MemoryStream(raw), Utf8, true)) json = reader.ReadToEnd();
            CheckShape(json);
            var doc = Json().Deserialize<Document>(json);
            Validate(folder, doc, true);
            // Hash the exact version parsed even if a concurrent writer has since replaced the file.
            using (var bytes = new MemoryStream(raw)) Revisions.GetOrCreateValue(doc).Hash = Hash(bytes);
            return doc;
        }

        public static Document Load(string folder) { return ReadDocument(folder, ProjectFile); }

        public static void InheritRevision(Document savedInstance, Document replacement)
        {
            Revision version;
            if (savedInstance != null && replacement != null && savedInstance.Id == replacement.Id && Revisions.TryGetValue(savedInstance, out version))
                Revisions.GetOrCreateValue(replacement).Hash = version.Hash;
        }

        public static void Save(string folder, Document doc)
        {
            Validate(folder, doc, false);
            Directory.CreateDirectory(folder);
            string filename = PathIn(folder, ProjectFile);
            string temp = PathIn(folder, ".checklist-" + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = PathIn(folder, Previous);
            // The project-local lock serializes writers only during a commit.
            using (var saveLock = new FileStream(PathIn(folder, ".save.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                if (File.Exists(filename))
                {
                    Revision version;
                    Require(Revisions.TryGetValue(doc, out version) && version.Hash == HashFile(filename),
                        "项目文件已变更或损坏。请重新打开核对，或将当前内容导出为备份。");
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                }
                else
                {
                    Require(!Directory.Exists(PathIn(folder,"images")) && !File.Exists(PathIn(folder,"tasks.json")), "请选择新的项目目录，避免覆盖旧资料。");
                }
                try
                {
                    byte[] serialized = Utf8.GetBytes(Json().Serialize(doc));
                    Require(serialized.Length <= MaxJson, "清单文字容量超出 32 MiB 上限。");
                    WriteNew(temp, serialized);
                    string digest = HashFile(temp);
                    PublishFile(temp, filename, backup);
                    Revisions.GetOrCreateValue(doc).Hash = digest;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        internal static string VerifyImage(string filename)
        {
            using (var input = File.OpenRead(filename))
            {
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                string ext = decoder is PngBitmapDecoder ? ".png" : decoder is JpegBitmapDecoder ? ".jpg" :
                    decoder is BmpBitmapDecoder ? ".bmp" : decoder is GifBitmapDecoder ? ".gif" :
                    decoder is TiffBitmapDecoder ? ".tif" : null;
                Require(ext != null && decoder.Frames.Count > 0, "图片格式不支持。支持 PNG、JPEG、BMP、GIF 和 TIFF。");
                foreach (var frame in decoder.Frames)
                {
                    long stride = ((long)frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
                    Require(stride > 0 && stride <= 16*1024*1024 && frame.PixelHeight > 0 && stride * frame.PixelHeight <= 1024L*1024*1024, "图片像素尺寸超限。");
                    int rows = Math.Min(frame.PixelHeight, Math.Max(1, 256*1024/(int)stride));
                    byte[] pixels = new byte[(int)stride*rows];
                    for (int y = 0; y < frame.PixelHeight; y += rows)
                        frame.CopyPixels(new System.Windows.Int32Rect(0,y,frame.PixelWidth,Math.Min(rows,frame.PixelHeight-y)), pixels, (int)stride, 0);
                }
                return ext;
            }
        }

        public static Asset AddFile(string folder, Document doc, string filename)
        {
            Require(doc != null && doc.Assets != null, "请选择项目。");
            string dir = PathIn(folder, "assets");
            Directory.CreateDirectory(dir);
            string temp = PathIn(folder, "assets/.incoming-" + Guid.NewGuid().ToString("N"));
            try
            {
                Copy(filename, temp);
                string ext = VerifyImage(temp);
                string digest = HashFile(temp);
                var asset = new Asset { RelativePath = "assets/" + digest + ext, OriginalName = Path.GetFileName(filename), Sha256 = digest };
                return PublishAsset(folder, doc, temp, asset);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static Asset PublishAsset(string folder, Document doc, string incoming, Asset asset)
        {
            Asset existing = doc.Assets.FirstOrDefault(a => a.Sha256 == asset.Sha256);
            if (existing != null)
            {
                Require(HashFile(AssetPath(folder, existing)) == asset.Sha256, "已有同内容素材损坏。");
                return existing;
            }
            Require(!doc.Assets.Any(a => a.Id == asset.Id), "暂存素材编号冲突。");
            string destination = AssetPath(folder, asset);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            try { File.Move(incoming, destination); }
            catch (IOException) { if (!File.Exists(destination) || HashFile(destination) != asset.Sha256) throw; }
            doc.Assets.Add(asset);
            return asset;
        }

        public static Asset AdoptStaged(string stage, string folder, Document doc, Asset staged)
        {
            string incoming = AssetPath(stage, staged);
            Require(staged.Sha256 != null && Regex.IsMatch(staged.Sha256, @"\A[0-9a-f]{64}\z")
                && !String.IsNullOrWhiteSpace(staged.Id) && !String.IsNullOrWhiteSpace(staged.OriginalName), "暂存素材登记无效。");
            if (File.Exists(incoming)) Require(HashFile(incoming) == staged.Sha256, "暂存原图校验失败。");
            else Require(doc.Assets.Any(a => a.Sha256 == staged.Sha256), "暂存原图已经缺失。");
            return PublishAsset(folder, doc, incoming, staged);
        }

        internal static IEnumerable<string> MigrationPaths(string folder)
        {
            bool exists = Directory.Exists(PathIn(folder, "migration"));
            if (!exists) return new string[0];
            var required = MigrationFiles.Where(rel => rel != "migration/progress.json").ToArray();
            Require(required.All(rel => File.Exists(PathIn(folder, rel))), "迁移记录不完整。");
            var files = Directory.EnumerateFileSystemEntries(PathIn(folder, "migration")).Select(p => "migration/" + Path.GetFileName(p)).ToArray();
            Require(files.All(rel => MigrationFiles.Contains(rel) && File.Exists(PathIn(folder, rel))), "迁移目录含无法识别的记录。");
            return files;
        }

        public static Document RestorePrevious(string source, string emptyTarget)
        {
            Outside(source, emptyTarget);
            var previous = ReadDocument(source, Previous);
            return Stage(emptyTarget, delegate(string stage)
            {
                foreach (var rel in previous.Assets.Select(a => a.RelativePath).Concat(MigrationPaths(source)))
                    Copy(PathIn(source, rel), PathIn(stage, rel));
                Save(stage, previous);
                return Load(stage);
            });
        }
    }
}
