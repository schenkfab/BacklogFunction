using System;
using System.Collections.Generic;

namespace BacklogFunction
{
    public class YouTubeFeed
    {
        public Feed feed { get; set; }
    }

    public class Feed
    {
        public string ChannelId { get; set; }
        public List<Entry> Entry { get; set; }
    }

    public class Link
    {
        public string Href { get; set; }
    }

    public class Entry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public Link Link { get; set; }
        public DateTime Published { get; set; }
        public Group Group { get; set; }
    }

    public class Group
    {
        public Thumbnail Thumbnail { get; set; }
        public string Description { get; set; }
    }

    public class Thumbnail
    {
        public string Url { get; set; }
    }
}