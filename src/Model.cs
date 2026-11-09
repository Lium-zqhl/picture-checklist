using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace PictureChecklist
{
    public class Document
    {
        public int Version { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Updated { get; set; }
        public bool Archived { get; set; }
        public List<Entry> Items { get; set; }
        public List<Asset> Assets { get; set; }

        public Document()
        {
            Version = 1;
            Id = Guid.NewGuid().ToString("N");
            Name = "新清单";
            Updated = DateTimeOffset.Now.ToString("o");
            Items = new List<Entry>();
            Assets = new List<Asset>();
        }
    }

    public class Entry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Caution { get; set; }
        public string Note { get; set; }
        public string Status { get; set; }
        public string Updated { get; set; }
        public bool NeedsConfirmation { get; set; }
        public bool Clarified { get; set; }
        public bool Deleted { get; set; }
        public int LegacyNumber { get; set; }
        public List<string> LegacyActions { get; set; }
        public List<string> AssetIds { get; set; }

        public Entry()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "新意见";
            Body = Caution = Note = "";
            Status = "todo";
            Updated = DateTimeOffset.Now.ToString("o");
            LegacyActions = new List<string>();
            AssetIds = new List<string>();
        }
    }

    public class Asset
    {
        public string Id { get; set; }
        public string RelativePath { get; set; }
        public string OriginalName { get; set; }
        public string Sha256 { get; set; }
        public Asset() { Id = Guid.NewGuid().ToString("N"); }
    }

    public static class ModelJson
    {
        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024, RecursionLimit = 64 };
        }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static T Read<T>(string value) { return Serializer().Deserialize<T>(value); }
    }
}
