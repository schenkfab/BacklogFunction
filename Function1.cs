using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace BacklogFunction
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async void Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function started executed at: {DateTime.Now}");

            // Get the connection string from app settings and use it to create a connection.
            var str = Environment.GetEnvironmentVariable("sqldb_connection");

            // Get all feeds that need to be crawled:
            List<Feed> feeds = await GetFeedsToCrawl(str, log);

            foreach (Feed feed in feeds)
            {
                log.LogInformation($"{feed.Url} feed being crawled");

                List<Article> articles = GetArticles(feed.Url);

                await SubmitArticles(articles, feed.Id, str, log);

                await FinishCrawl(feed.Id, str, log);
            }


            log.LogInformation($"C# Timer trigger function finished executed at: {DateTime.Now}");

        }

        public static List<Article> GetArticles(string url)
        {
            List<Article> articles = new List<Article>();

            XmlReader reader = XmlReader.Create(url);
            SyndicationFeed feed = SyndicationFeed.Load(reader);
            reader.Close();
            foreach (SyndicationItem item in feed.Items)
            {
                string subject = item.Title.Text;
                string summary = item.Summary.Text;
                string image = feed.ImageUrl == null ? null : feed.ImageUrl.ToString();
                string link = item.Links[0].Uri.ToString();
                string created = item.PublishDate.UtcDateTime.ToString("yyyy-MM-dd HH':'mm':'ss");

                articles.Add(new Article() { Name = subject, Description = summary, Image = image, Link = link, Created = created });
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
