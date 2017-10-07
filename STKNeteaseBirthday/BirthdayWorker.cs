using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using StalkerProject;
using System.IO;
using NeteaseBirthdayAlbum;
using Newtonsoft.Json.Linq;
using SQLite;
using SQLite.Net;
using SQLite.Net.Platform.Generic;

namespace StalkerProject
{
    class BirthdayWorker : DomainProxy
    {

        public string BirthdaysFile { get; set; }
        public int StartIndex { get; set; }
        private List<DateTime> birthdays;
        private NeteaseAlbumFetch fetch;
        private SQLiteConnection conn;

        public BirthdayWorker() : base()
        {
            birthdays=new List<DateTime>();
            fetch = new NeteaseAlbumFetch();
        }

        #region Overrides of DomainProxy

        public override void Stop()
        {
            base.Stop();
            fetch.Stop();
        }


        /// <summary>调用后加载对这个服务而言的默认配置</summary>
        public override void LoadDefaultSetting()
        {
            base.LoadDefaultSetting();
            Alias = "BirthdayWorker" + new Random().Next(1, 10000);
        }


        public override void Start()
        {
            base.Start();
            base.OnRequest += HandleHttpRequest;
            if (File.Exists(BirthdaysFile))
            {
                foreach (var birthday in File.ReadAllLines(BirthdaysFile))
                {
                    try
                    {
                        DateTime date = DateTime.Parse(birthday);
                        birthdays.Add(date);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Module {Alias} : Invalid Date {birthday}");
                    }
                }
            }
            conn = new SQLiteConnection(new SQLitePlatformGeneric(),$"{Alias}.db");
            conn.CreateTable<DataItem>();
            fetch.Index = Math.Max(fetch.Index, StartIndex);
            if (conn.ExecuteScalar<int>("select count(*) from DataItem") > 0)
                fetch.Index = Math.Max(fetch.Index, conn.ExecuteScalar<int>("select max(ID) from DataItem") + 1);
            fetch.EndIndex = fetch.GetMaxAlbumId();
            fetch.onFetched += onFetched;
            fetch.onFailed += onFail;
            fetch.Start();
        }

        #endregion

        private int failCount = 0;
        private int succCount = 0;

        private void onFail(int id)
        {
            failCount++;
            if (failCount % 10000 == 0)
            {
                BirthdayMatched?.Invoke($"http://music.163.com/m/album?id={id}",
                             $"获取失败次数已达到{failCount},成功率{(float)(failCount*100)/(succCount+failCount)}", $"请检查服务状态", DateTime.Now.ToString());
                failCount = 0;
                succCount = 0;
            }
        }

        public void HandleHttpRequest(HttpListenerContext request,string SubUrl)
        {
            try
            {
                string rawurl = request.Request.RawUrl;
                rawurl = rawurl.Replace(SubUrl, "");
                rawurl = rawurl.Replace("\\", "/");
                if (rawurl.Length > 1 && (rawurl[0] == '/' || rawurl[0] == '\\'))
                    rawurl = rawurl.Substring(1);
                var ymd = rawurl.Split('/');
                if (ymd.Length < 3)
                {
                    request.ResponseString($"Status : Fetching ID {fetch.Index}\n FailCount : {failCount} SuccessCount:{succCount}");
                    return;
                }
                DateTime date = new DateTime(int.Parse(ymd[0]), int.Parse(ymd[1]), int.Parse(ymd[2])).AddHours(-8);//chn to utc
                long src = date.ToUnixTime();
                long dst = date.AddDays(1).ToUnixTime();
                var results = from p in conn.Table<DataItem>()
                              where p.PublishTime >= src && p.PublishTime < dst
                              select p;
                JObject obj = new JObject();
                var result = new JArray();
                obj.Add("data", result);
                foreach (var item in results)
                {
                    JObject info = new JObject()
                        {
                            {"name", item.Name},
                            {"artist", item.Artist},
                            {"publishdate", item.PublishTime.ToString()},
                            {"id", item.ID},
                            {"href", $"http://music.163.com/m/album?id={item.ID}"}
                        };
                    result.Add(info);
                }
                /*
                request.ResponseString(
                    "<html><head><meta content=\"text/html; charset=utf-8\" http-equiv=\"content-type\" /></head><body>" +
                    obj.ToString().Replace("\n", "<br>") +
                    "</body></html>");
                    */
                request.ResponseString(obj.ToString());
            }
            catch (Exception e)
            {
                request.ResponseString("Invalid String...");
            }
        }

        private void onFetched(string name,string artist,DateTime pubTime,int id)
        {
            try
            {
                succCount++;
                foreach (var birthday in birthdays)
                {
                    var tmp = birthday.AddHours(-8);//to utc(chn)
                    if (tmp.Year == pubTime.Year &&
                        tmp.Month == pubTime.Month &&
                        tmp.Day == pubTime.Day)
                    {
                        BirthdayMatched?.Invoke($"http://music.163.com/m/album?id={id}",
                            "找到了发布日期与生日相同的专辑!", $"是 {name}\t{artist}\t{pubTime}\t{id}", DateTime.Now.ToString());
                        Console.WriteLine($"WOW!!! \n{name}\t{artist}\t{pubTime}\t{id}");
                    }
                }
                conn.Insert(new DataItem
                {
                    ID = id,
                    Name = name,
                    Artist = artist,
                    PublishTime = pubTime.ToUnixTime()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e} \n{name}\t{artist}\t{pubTime}\t{id}");
                throw;
            }
            
        }
        public Action<string, string, string, string> BirthdayMatched { get; set; }
    }
    
}
