using System;
using System.IO;
using System.Net;
using System.Text;

namespace Harbor
{
    /// <summary>Plain HTTPS GET for Harbor's few web lookups (lyrics, weather). It blocks, so call it off the UI thread.</summary>
    static class Web
    {
        public static string Get(string url, out int status)
        {
            // .NET Framework apps built without a target-framework attribute default to old TLS versions
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.3
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Harbor/" + AppInfo.Version + " (" + AppInfo.RepoUrl + ")";
            req.Timeout = 6000;
            req.ReadWriteTimeout = 6000;
            // gzip only: .NET Framework misreads servers' "deflate" (zlib-wrapped) replies - Open-Meteo sends those
            req.AutomaticDecompression = DecompressionMethods.GZip;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)resp.StatusCode;
                    return rd.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                status = resp != null ? (int)resp.StatusCode : 0;
                if (resp != null) resp.Close();
                return null;
            }
        }
    }
}
