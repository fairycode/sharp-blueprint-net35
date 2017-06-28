
using System;

namespace SharpBlueprint.Client
{
    public class Environment
    {
        public string GetFrameworkVersion()
        {
            return ".NET Framework 3.5";
        }

        public static void UploadCoverageReport(string openCoverXml)
        {
            const string url = "https://codecov.io/upload/v2";

            // query parameters: https://github.com/codecov/codecov-bash/blob/master/codecov#L1202
            var queryBuilder = new System.Text.StringBuilder(url);
            queryBuilder.Append("?package=bash-tbd&service=appveyor");
            queryBuilder.Append("&branch=").Append(context.EnvironmentVariable("APPVEYOR_REPO_BRANCH"));
            queryBuilder.Append("&commit=").Append(context.EnvironmentVariable("APPVEYOR_REPO_COMMIT"));
            queryBuilder.Append("&build=").Append(context.EnvironmentVariable("APPVEYOR_JOB_ID"));
            queryBuilder.Append("&pr=").Append(context.EnvironmentVariable("APPVEYOR_PULL_REQUEST_NUMBER"));
            queryBuilder.Append("&job=").Append(context.EnvironmentVariable("APPVEYOR_ACCOUNT_NAME"));
            queryBuilder.Append("%2F").Append(context.EnvironmentVariable("APPVEYOR_PROJECT_SLUG"));
            queryBuilder.Append("%2F").Append(context.EnvironmentVariable("APPVEYOR_BUILD_VERSION"));
            queryBuilder.Append("&token=").Append(context.EnvironmentVariable("CODECOV_TOKEN"));

            var request = (System.Net.HttpWebRequest) System.Net.WebRequest.Create(queryBuilder.ToString());

            request.ContentType = "";
            request.Accept = "application/json";
            request.Method = "POST";

            using (var requestStream = request.GetRequestStream())
            using (var openCoverXmlStream =
                new System.IO.FileStream(openCoverXml, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                var buffer = new byte[1024];
                int readBytes;
                while ((readBytes = openCoverXmlStream.Read(buffer, 0, buffer.Length)) > 0)
                    requestStream.Write(buffer, 0, readBytes);
            }

            using (var response = (System.Net.HttpWebResponse) request.GetResponse())
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    using (var responseStream = response.GetResponseStream())
                    {
                        if (responseStream != null)
                        {
                            using (var responseStreamReader = new System.IO.StreamReader(responseStream))
                                context.Information(responseStreamReader.ReadToEnd());
                        }
                    }
                }
                else
                {
                    context.Information("Status code: " + response.StatusCode);
                }
            }
        }
    }
}
