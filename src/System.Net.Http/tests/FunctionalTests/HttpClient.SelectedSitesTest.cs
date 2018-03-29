// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace System.Net.Http.Functional.Tests
{
    public abstract class HttpClient_SelectedSites_Test : HttpClientTestBase
    {
        static HttpClient_SelectedSites_Test()
        {
            EncodingProvider provider = CodePagesEncodingProvider.Instance;
            Encoding.RegisterProvider(provider);
        }

        [Theory]
        [OuterLoop]
        [Trait("SelectedSites", "true")]
        [MemberData(nameof(GetSelectedSites))]
        public async Task RetrieveSite_Succeeds(string site)
        {
            // Not doing this in bulk for platform handlers.
            if (!UseSocketsHttpHandler)
                return;

            int remainingAttempts = 2;
            while (remainingAttempts-- > 0)
            {
                try
                {
                    await VisitSite(site, getLinks:true);
                    return;
                }
                catch
                {
                    if (remainingAttempts < 1)
                        throw;
                    await Task.Delay(1500);
                }
            }

            throw new Exception("Not expected to reach here");
        }

        [Theory]
        [OuterLoop]
        [Trait("SiteInvestigation", "true")]
        [InlineData("http://vip.qzone.com/")]
        [InlineData("http://jubao.china.cn:13225/reportform.do")]
        [InlineData("http://www.bj.cyberpolice.cn/index.htm")]
        [InlineData("http://sq.ccm.gov.cn/ccnt/sczr/service/business/emark/toDetail/0D76560AE65141FF9FEFE3481D205C50")]
        [InlineData("https://r.mradx.net")]
        [InlineData("https://hellonetwork.app.link/VUnS6L9JpG")]
        [InlineData("http://sq.ccm.gov.cn/ccnt/sczr/service/business/emark/toDetail/DFB957BAEB8B417882539C9B9F9547E6")]
        [InlineData("https://www.aliloan.com/")]
        [InlineData("http://yelp.com")]
        [InlineData("http://www.024zol.com")]
        [InlineData("http://veoh.tv/ccjjew")]
        [InlineData("https://www.theweathercompany.com/newsroom")]
        [InlineData("http://bj429.com.cn")]
        [InlineData("https://mbp.yimg.com/sy/os/mit/media/p/common/images/favicon_new-7483e38.svg")]
        [InlineData("http://www.baohejr.com")]
        [InlineData("http://www.bj.cyberpolice.cn/index.jsp")]
        [InlineData("http://wza.chinanews.com/")]
        [InlineData("http://www.dailylalala.com/")]
        [InlineData("http://www.mefeedianetwork.com")]
        [InlineData("http://careers.citygrid.com/")]
        [InlineData("http://www.letv.com/")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        //[InlineData("")]
        public async Task RetrieveSite_Debug_Helper(string site)
        {
            await VisitSite(site);
        }

        public static IEnumerable<string[]> GetSelectedSites()
        {
            const string resourceName = "SelectedSitesTest.txt";
            Assembly assembly = typeof(HttpClient_SelectedSites_Test).Assembly;
            Stream s = assembly.GetManifestResourceStream(resourceName);
            if (s == null)
            {
                throw new Exception("Couldn't find resource " + resourceName);
            }

            using (var reader = new StreamReader(s))
            {
                string site;
                while (null != (site = reader.ReadLine()))
                {
                    yield return new[] { site };
                }
            }
        }

        private async Task VisitSite(string site, bool getLinks = false)
        {
            using (HttpClient httpClient = CreateHttpClientForSiteVisit())
            {
                if (!getLinks)
                {
                    httpClient.DefaultRequestHeaders.Add(
                        "Accept-Encoding",
                        "gzip, deflate, br");
                }

                HashSet<string> links = await VisitSiteWithClient(site, httpClient, getLinks);

                if (getLinks)
                {
                    httpClient.DefaultRequestHeaders.Add(
                        "Accept-Encoding",
                        "gzip, deflate, br");
                }

                var linkExceptions = new List<string>();
                foreach (string link in links)
                {
                    try
                    {
                        await VisitSiteWithClient(link, httpClient);
                    }
                    catch (Exception e)
                    {
                        linkExceptions.Add($"Link:{link} => {e.Message}");
                    }
                }

                Console.WriteLine($"_attempts:{s_attempts} _successVisits:{s_successVisits}");
                if (linkExceptions.Count > 0)
                    throw new Exception($"Links exception for site {site}: {string.Join(" | ", linkExceptions)}");
            }
        }

        private static int s_attempts = 0;
        private static int s_successVisits = 0;

        private async Task<HashSet<string>> VisitSiteWithClient(string site, HttpClient httpClient, bool getLinks = false)
        {
            const int maxChildLinks = 16;
            var links = new HashSet<string>(maxChildLinks);
            string host = new Uri(site).Host;

            Interlocked.Increment(ref s_attempts);
            using (HttpResponseMessage response = await httpClient.GetAsync(site))
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Redirect:
                    case HttpStatusCode.OK:
                        if (getLinks && response.Content.Headers.ContentLength > 0)
                        {
                            string page;
                            try
                            {
                                page = await response.Content.ReadAsStringAsync();
                            }
                            catch (InvalidOperationException)
                            {
                                // Atempt UTF-8
                                page = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
                            }

                            var hrefIdx = 0;
                            const string hrefPrefix = "href=\"";
                            while (links.Count < maxChildLinks && (hrefIdx = page.IndexOf(hrefPrefix, hrefIdx)) != -1)
                            {
                                var linkStartIdx = hrefIdx + hrefPrefix.Length;
                                var hrefEndIdx = page.IndexOf('"', linkStartIdx);
                                var link = page.Substring(linkStartIdx, hrefEndIdx - linkStartIdx);
                                if (link.StartsWith("http") && !link.Contains(host))
                                {
                                    links.Add(link);
                                }

                                hrefIdx = hrefEndIdx;
                            }

                        }
                        Interlocked.Increment(ref s_successVisits);
                        break;
                    case HttpStatusCode.GatewayTimeout:
                    case HttpStatusCode.BadRequest: // following links may cause this for missing parameters
                    case HttpStatusCode.BadGateway:
                    case HttpStatusCode.Forbidden:
                    case HttpStatusCode.Moved:
                    case HttpStatusCode.NoContent:
                    case HttpStatusCode.NotFound:
                    case HttpStatusCode.ServiceUnavailable:
                    // case HttpStatusCode.TemporaryRedirect:
                    case HttpStatusCode.Unauthorized:
                    case HttpStatusCode.InternalServerError:
                        Interlocked.Increment(ref s_successVisits);
                        break;
                    default:
                        throw new Exception($"{site} returned: {response.StatusCode}");
                }
            }

            return links;
        }

        private HttpClient CreateHttpClientForSiteVisit()
        {
            HttpClient httpClient = new HttpClient(CreateHttpClientHandler(UseSocketsHttpHandler));

            // Some extra headers since some sites only give proper responses when they are present.
            httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.186 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add(
                "Accept-Language",
                "en-US,en;q=0.9");
            httpClient.DefaultRequestHeaders.Add(
                "Accept-Encoding",
                "gzip, deflate, br");
            httpClient.DefaultRequestHeaders.Add(
                "Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");

            return httpClient;
        }
    }
}
