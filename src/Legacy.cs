using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PictureChecklist
{
    public static class Legacy
    {
        private sealed class Row
        {
            public Entry Item;
            public string Image;
        }

        private sealed class Source
        {
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<string> ExtraKeys = new List<string>();
            public bool HasProgress;
            public string TasksHash;
            public string ProgressHash;
            public readonly Dictionary<string,string> Images = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string Text(Dictionary<string,object> map, string key, string context)
        {
            object value;
            Store.Require(map.TryGetValue(key, out value) && value is string, context + "缺少文本字段 " + key);
            return (string)value;
        }

        private static bool Flag(Dictionary<string,object> map, string key, string context)
        {
            object value;
            Store.Require(map.TryGetValue(key, out value) && value is bool, context + "缺少布尔字段 " + key);
            return (bool)value;
        }

        private static object ReadSnapshot(string path, out string hash)
        {
            Store.Require(new FileInfo(path).Length <= Store.MaxJson, "旧 JSON 文件过大。");
            byte[] bytes = File.ReadAllBytes(path);
            using (var data = new MemoryStream(bytes)) hash = Store.Hash(data);
            using (var reader = new StreamReader(new MemoryStream(bytes), Store.Utf8, true))
                return Store.Json().DeserializeObject(reader.ReadToEnd());
        }

        private static void Extras(Source result, Dictionary<string,object> map, string context, params string[] known)
        {
            foreach (var key in map.Keys.Except(known)) result.ExtraKeys.Add(context + "." + key);
        }

        private static IEnumerable<string> ImageFiles(string root, string directory)
        {
            string path = Store.PathIn(root, directory);
            if (!Directory.Exists(path)) yield break;
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                string rel = directory + "/" + Path.GetFileName(child);
                Store.PathIn(root, rel);
                if (Directory.Exists(child))
                {
                    foreach (var descendant in ImageFiles(root, rel)) yield return descendant;
                }
                else yield return rel;
            }
        }

        private static Source Inspect(string folder)
        {
            var result = new Source();
            string tasksFile = Store.PathIn(folder, "tasks.json"), progressFile = Store.PathIn(folder, "progress.json");
            Store.Require(File.Exists(tasksFile), "旧目录缺少 tasks.json。");
            var records = Store.ArrayValue(ReadSnapshot(tasksFile, out result.TasksHash), "旧任务列表");
            var progress = new Dictionary<string,object>();
            result.HasProgress = File.Exists(progressFile);
            if (result.HasProgress)
            {
                var wrapper = Store.Object(ReadSnapshot(progressFile, out result.ProgressHash), "旧进度");
                object version, items;
                Store.Require(wrapper.TryGetValue("version", out version) && version is int && (int)version == 1 && wrapper.TryGetValue("items", out items), "未知的旧进度格式或版本。");
                progress = Store.Object(wrapper["items"], "旧进度 items");
                Extras(result, wrapper, "progress", "version", "items");
            }
            var ids = new HashSet<int>();
            foreach (var raw in records)
            {
                var map = Store.Object(raw, "旧条目");
                object id, actions;
                Store.Require(map.TryGetValue("id", out id) && id is int && (int)id > 0 && ids.Add((int)id), "旧编号无效或重复。");
                string number = ((int)id).ToString(CultureInfo.InvariantCulture), context = "旧条目 " + number;
                Store.Require(map.TryGetValue("actions", out actions) && Store.ArrayValue(actions, context + ".actions").All(x => x is string), context + " 动作无效。");
                var entry = new Entry { LegacyNumber = (int)id, Title = Text(map,"title",context), LegacyActions = ((object[])actions).Cast<string>().ToList(),
                    Caution = Text(map,"caution",context), NeedsConfirmation = Flag(map,"needsConfirmation",context), Updated = "" };
                entry.Body = String.Join(Environment.NewLine, entry.LegacyActions);
                Extras(result, map, context, "id", "title", "actions", "caution", "needsConfirmation", "image");
                object state;
                if (progress.TryGetValue(number, out state))
                {
                    var values = Store.Object(state, context + "进度");
                    entry.Status = Flag(values, "done", context) ? "done" : "todo";
                    entry.Clarified = Flag(values, "clarified", context);
                    entry.Note = Text(values, "note", context);
                    entry.Updated = Text(values, "updated", context);
                    Extras(result, values, context + ".progress", "done", "clarified", "note", "updated");
                }
                string image = Text(map, "image", context).Replace('\\','/');
                Store.Require(image.StartsWith("images/", StringComparison.OrdinalIgnoreCase), context + " 的图片路径无效。");
                string imagePath = Store.PathIn(folder, image);
                Store.Require(File.Exists(imagePath), context + " 缺少原图：" + image);
                if (!result.Images.ContainsKey(image))
                {
                    Store.VerifyImage(imagePath);
                    result.Images.Add(image, Store.HashFile(imagePath));
                }
                result.Rows.Add(new Row { Item = entry, Image = image });
            }
            foreach (var key in progress.Keys)
            {
                int number;
                Store.Require(Int32.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
                    key == number.ToString(CultureInfo.InvariantCulture) && ids.Contains(number), "进度存在没有对应旧条目的编号：" + key);
            }
            foreach (var rel in ImageFiles(folder, "images"))
                Store.Require(result.Images.ContainsKey(rel), "旧图片目录含未关联的文件，请先核对：" + rel);
            return result;
        }

        private static string Summary(Source state)
        {
            return String.Format("共 {0} 条意见、{1} 张来源图片。已完成 {2} 条，待澄清 {3} 条，有备注 {4} 条。",
                state.Rows.Count, state.Images.Count, state.Rows.Count(r => r.Item.Status == "done"),
                state.Rows.Count(r => r.Item.NeedsConfirmation && !r.Item.Clarified), state.Rows.Count(r => r.Item.Note.Length > 0));
        }

        public static string Preview(string oldFolder)
        {
            Source parsed = Inspect(oldFolder);
            return Summary(parsed) + Environment.NewLine + (parsed.HasProgress ? "旧进度及窗口设置将随旧 JSON 封存。" : "未找到 progress.json，条目按未开始导入。")
                + Environment.NewLine + "导入时会复制原图并逐张比对 SHA-256。";
        }

        public static Document Import(string oldFolder, string emptyTarget)
        {
            Store.Outside(oldFolder, emptyTarget);
            Source parsed = Inspect(oldFolder);
            return Store.Stage(emptyTarget, delegate(string stage)
            {
                var doc = new Document { Name = "迁移清单 · " + new DirectoryInfo(oldFolder).Name };
                foreach (var rel in Store.MigrationFiles.Where(r => r != "migration/report.json"))
                {
                    if (rel == "migration/progress.json" && !parsed.HasProgress) continue;
                    string original = Store.PathIn(oldFolder, Path.GetFileName(rel)), copy = Store.PathIn(stage, rel);
                    Store.Copy(original, copy);
                    Store.Require(Store.HashFile(copy) == (rel == "migration/tasks.json" ? parsed.TasksHash : parsed.ProgressHash), "迁移期间旧 JSON 已变化。");
                }
                var ledger = new List<object>();
                foreach (var row in parsed.Rows)
                {
                    var entry = row.Item;
                    var asset = Store.AddFile(stage, doc, Store.PathIn(oldFolder, row.Image));
                    Store.Require(asset.Sha256 == parsed.Images[row.Image], "迁移期间旧图已变化：" + row.Image);
                    entry.AssetIds.Add(asset.Id); doc.Items.Add(entry);
                    ledger.Add(new { LegacyNumber = entry.LegacyNumber, EntryId = entry.Id, Image = row.Image, AssetId = asset.Id,
                        Sha256 = asset.Sha256, OriginalName = asset.OriginalName, Status = entry.Status, NeedsConfirmation = entry.NeedsConfirmation,
                        Clarified = entry.Clarified, Updated = entry.Updated });
                }
                var report = new { Version = 1, ImportedAt = DateTimeOffset.Now.ToString("o"), Summary = Summary(parsed),
                    SourceTasksSha256 = parsed.TasksHash, SourceProgressSha256 = parsed.ProgressHash,
                    ItemCount = doc.Items.Count, SourceImageCount = parsed.Images.Count, AssetCount = doc.Assets.Count,
                    ArchivedAdditionalFields = parsed.ExtraKeys, Items = ledger };
                Store.WriteNew(Store.PathIn(stage, "migration/report.json"), Store.Utf8.GetBytes(Store.Json().Serialize(report)));
                Store.Save(stage, doc);
                return Store.Load(stage);
            });
        }
    }
}
