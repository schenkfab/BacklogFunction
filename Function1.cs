using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BacklogFunction
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async void Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
        {
            
                log.LogInformation($"C# Timer trigger function started executed at: {DateTime.Now}");

                // Get the connection string from app settings and use it to create a connection.
                var str = Environment.GetEnvironmentVariable("sqldb_connection");

                // Get all feeds that need to be crawled:
                List<Feed> feeds = await GetFeedsToCrawl(str, log);

                //Feed f = new Feed();
                //f.Id = 9999;
                //f.Url = "http://feeds.feedburner.com/MSSQLTips-LatestSqlServerTips";
                //feeds = new List<Feed>();
                //feeds.Add(f);

                foreach (Feed feed in feeds)
                {
                    try
                    {
                        log.LogInformation($"{feed.Url} feed being crawled");

                        List<Article> articles = GetArticles(feed.Url, log);

                        await SubmitArticles(articles, feed.Id, str, log);

                        await FinishCrawl(feed.Id, str, log);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex.Message);
                    }
                }
                log.LogInformation($"C# Timer trigger function finished executed at: {DateTime.Now}");
            
        }

        public static List<Article> GetArticles(string url, ILogger log)
        {
            List<Article> articles = new List<Article>();
            if (url.Contains("youtube.com"))
            {
                //XmlSerializer ser = new XmlSerializer(typeof(YouTubeFeed));

                WebClient client = new WebClient();

                string data = Encoding.Default.GetString(client.DownloadData(url));

                Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

                //var document = (YouTubeFeed)ser.Deserialize(stream);
                XmlReader reader = XmlReader.Create(url);
                XmlDocument doc = new XmlDocument();
                doc.Load(reader);

                string json = JsonConvert.SerializeXmlNode(doc);
                json = json.Replace("@href", "href").Replace("yt:channelId", "channelId").Replace("media:", "").Replace("@url", "url");

                YouTubeFeed yt = JsonConvert.DeserializeObject<YouTubeFeed>(json);
                BacklogFunction.Feed feed = yt.feed;

                foreach (Entry item in feed.Entry)
                {
                    string subject = item.Title;
                    string summary = item.Group.Description;
                    string image = item.Group.Thumbnail.Url;
                    string link = item.Link.Href;
                    string created = item.Published.ToString("yyyy-MM-dd HH':'mm':'ss");

                    articles.Add(new Article() { Name = subject, Description = summary, Image = image, Link = link, Created = created });
                }

            } else
            {
                XmlReader reader = XmlReader.Create(url);
                SyndicationFeed feed = SyndicationFeed.Load(reader);
                reader.Close();
                foreach (SyndicationItem item in feed.Items)
                {
                    string subject = item.Title.Text;
                    string summary = item.Summary == null ? "" : item.Summary.Text;
                    string image = feed.ImageUrl == null ? null : feed.ImageUrl.ToString();
                    string link = item.Links[0].Uri.ToString();
                    try
                    {
                        string created = item.PublishDate.UtcDateTime.ToString("yyyy-MM-dd HH':'mm':'ss");
                        articles.Add(new Article() { Name = subject, Description = summary, Image = image, Link = link, Created = created });
                    } catch (Exception ex)
                    {
                        log.LogWarning("Could not add: " + item.Title.Text + " from: " + url + " because of: " + ex.Message);
                    }
                }
            }
           
            return articles;
        }

        public class Article
        {
            public string Name { get; set; }
            public string Image { get; set; }
            public string Description { get; set; }
            public string Created { get; set; }
            public string Link { get; set; }
        }

        public class Feed
        {
            public string Url { get; set; }
            public int Id { get; set; }
        }

        public static async Task<List<Feed>> GetFeedsToCrawl(string connectionString, ILogger log)
        {
            var sqlCommandText = "SELECT Id, Url FROM dbo.CrawlJobs";
            List<Feed> feeds = new List<Feed>();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (SqlCommand cmd = new SqlCommand(sqlCommandText, conn))
                {
                    SqlDataReader rdr = await cmd.ExecuteReaderAsync();
                    while (rdr.Read())
                    {
                        feeds.Add(new Feed() { Url = rdr["Url"].ToString(), Id = Int32.Parse(rdr["Id"].ToString()) });
                    }
                }
            }

            return feeds;
        }

        public static async Task<bool> SubmitArticles(List<Article> articles, int feedId, string connectionString, ILogger log)
        {
            foreach(Article article in articles)
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var storedProcedure = "dbo.usp_AddArticle";

                    using (SqlCommand sp = new SqlCommand(storedProcedure, conn))
                    {
                        // 2. set the command object so it knows to execute a stored procedure
                        sp.CommandType = CommandType.StoredProcedure;

                        // 3. add parameter to command, which will be passed to the stored procedure
                        sp.Parameters.Add(new SqlParameter("@Name", article.Name));
                        sp.Parameters.Add(new SqlParameter("@Picture", article.Image));
                        sp.Parameters.Add(new SqlParameter("@Description", article.Description));
                        sp.Parameters.Add(new SqlParameter("@Date", article.Created));
                        sp.Parameters.Add(new SqlParameter("@FeedId", feedId));
                        sp.Parameters.Add(new SqlParameter("@Link", article.Link));

                        // execute the command
                        var art = await sp.ExecuteNonQueryAsync();
                    }
                }
            }
            return true;
        }

        public static async Task<bool> FinishCrawl(int feedId, string connectionString, ILogger log)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string storedProcedure = "dbo.usp_FinishCrawl";

                using (SqlCommand sp = new SqlCommand(storedProcedure, conn))
                {
                    sp.CommandType = CommandType.StoredProcedure;
                    sp.Parameters.Add(new SqlParameter("@FeedId", feedId));

                    await sp.ExecuteNonQueryAsync();
                }
            }

            return true;
        }
    }
}
