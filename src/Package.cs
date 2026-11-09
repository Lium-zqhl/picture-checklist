using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace PictureChecklist
{
    public static class Package
    {
        private const string Catalog = "manifest.json";
        private const string Format = "PictureChecklist.Backup";
        private const long MaxMember = 512L * 1024 * 1024;
        private const long MaxTotal = 2L * 1024 * 1024 * 1024;

        private static void Put(ZipArchive archive, string relative, Stream input, IDictionary<string,string> hashes, string expectedHash)
        {
            long max = relative.StartsWith("assets/") ? MaxMember : Store.MaxJson;
            Store.Require(input.Length <= max, "备份文件过大：" + relative);
            var member = archive.CreateEntry(relative, relative.StartsWith("assets/") ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using (var output = member.Open()) input.CopyTo(output);
            input.Position = 0;
            string hash = Store.Hash(input);
            if (expectedHash != null) Store.Require(hash == expectedHash, "原图校验失败：" + relative);
            hashes.Add(relative, hash);
        }

        // Both import and export verification apply identical path, count, size and digest rules.
        private static void Verify(ZipArchive archive, string destination)
        {
            Store.Require(archive.Entries.Count >= 2 && archive.Entries.Count <= 100000, "备份文件数无效。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var member in archive.Entries)
            {
                string rel = member.FullName;
                Store.SafeRelative(rel);
                bool asset = rel.StartsWith("assets/", StringComparison.Ordinal) && rel.Split('/').Length == 2;
                Store.Require(names.Add(rel) && (asset || rel == Catalog || rel == Store.ProjectFile || Store.MigrationFiles.Contains(rel)),
                    "重复或无法识别的备份成员：" + rel);
                Store.Require(member.Length <= (asset ? MaxMember : Store.MaxJson) && member.Length <= MaxTotal - total, "备份解包大小超限。");
                total += member.Length;
            }
            Store.Require(names.Contains(Catalog) && names.Contains(Store.ProjectFile), "缺少备份索引或清单。");
            Dictionary<string,object> meta;
            using (var reader = new StreamReader(archive.GetEntry(Catalog).Open(), Store.Utf8, true))
                meta = Store.Object(Store.Json().DeserializeObject(reader.ReadToEnd()), "备份索引");
            Store.Keys(meta, "Format", "Version", "Files");
            Store.Require(meta["Format"] as string == Format && meta["Version"] is int && (int)meta["Version"] == 1, "无法识别的备份格式或版本。");
            var inventory = Store.Object(meta["Files"], "备份文件列表");
            Store.Require(inventory.Count == archive.Entries.Count - 1, "备份文件数量与索引不一致。");
            foreach (var member in archive.Entries.Where(x => x.FullName != Catalog))
            {
                object digest;
                Store.Require(inventory.TryGetValue(member.FullName, out digest) && digest is string && Regex.IsMatch((string)digest, @"\A[0-9a-f]{64}\z"), "缺少文件校验值：" + member.FullName);
                string actual;
                using (var input = member.Open())
                {
                    if (destination == null) actual = Store.Hash(input);
                    else
                    {
                        string path = Store.PathIn(destination, member.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            int read;
                            long length = 0;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                length += read;
                                Store.Require(length <= member.Length, "成员超出声明长度：" + member.FullName);
                                file.Write(buffer, 0, read);
                            }
                            Store.Require(length == member.Length, "成员长度不符：" + member.FullName);
                        }
                        actual = Store.HashFile(path);
                    }
                }
                Store.Require(actual == (string)digest, "备份文件校验失败：" + member.FullName);
            }
        }

        public static void Export(string folder, Document doc, string zipPath)
        {
            Store.Validate(folder, doc, false);
            string target = Path.GetFullPath(zipPath);
            string parent = Path.GetDirectoryName(target);
            Store.Require(Directory.Exists(parent), "备份目标目录不存在。");
            Store.Require(String.Equals(Path.GetExtension(target), ".zip", StringComparison.OrdinalIgnoreCase), "请使用 .zip 备份扩展名。");
            Store.NoLink(target);
            foreach (string managed in new[] { "assets", "migration", "backups" })
                Store.Outside(Store.PathIn(folder, managed), target);
            string temp = Path.Combine(parent, ".checklist-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    var hashes = new Dictionary<string,string>(StringComparer.Ordinal);
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
                    {
                        using (var bytes = new MemoryStream(Store.Utf8.GetBytes(Store.Json().Serialize(doc))))
                            Put(zip, Store.ProjectFile, bytes, hashes, null);
                        foreach (var a in doc.Assets)
                            using (var input = File.OpenRead(Store.AssetPath(folder, a))) Put(zip, a.RelativePath, input, hashes, a.Sha256);
                        foreach (var rel in Store.MigrationPaths(folder))
                            using (var input = File.OpenRead(Store.PathIn(folder, rel))) Put(zip, rel, input, hashes, null);
                        var index = new { Format = Format, Version = 1, Files = hashes };
                        var indexMember = zip.CreateEntry(Catalog);
                        using (var writer = new StreamWriter(indexMember.Open(), Store.Utf8)) writer.Write(Store.Json().Serialize(index));
                    }
                    file.Flush(true);
                }
                using (var input = File.OpenRead(temp))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read)) Verify(zip, null);
                Store.PublishFile(temp, target, null);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static Document Import(string zipPath, string emptyTarget)
        {
            return Store.Stage(emptyTarget, delegate(string stage)
            {
                using (var input = File.OpenRead(zipPath))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    Verify(zip, stage);
                    Document doc = Store.Load(stage);
                    var expected = new HashSet<string>(doc.Assets.Select(a => a.RelativePath), StringComparer.Ordinal);
                    expected.UnionWith(Store.MigrationPaths(stage));
                    expected.Add(Store.ProjectFile); expected.Add(Catalog);
                    Store.Require(expected.SetEquals(zip.Entries.Select(e => e.FullName)), "备份素材登记与实际文件不一致。");
                    foreach (var a in doc.Assets)
                        Store.VerifyImage(Store.AssetPath(stage, a));
                    return doc;
                }
            });
        }
    }
}
