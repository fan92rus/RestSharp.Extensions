using System;
using System.Reflection;
using Extensions.RestSharp.Annotations;
using global::RestSharp;
using Polly;

namespace Extensions.RestSharp
{
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Web;

    /// <summary>
    /// Расширения для RestSharper
    /// </summary>
    public static class RestExtension
    {
        /// <summary>
        /// Добавление параметров в запрос из обьекта
        /// </summary>
        /// <param name="request">запрос</param>
        /// <param name="obj">обьект для получения параметров</param>
        /// <returns>Запрос с установлеными параметрами</returns>
        public static IRestRequest AddParamFromObject(this IRestRequest request, object obj)
        {
            var properties = obj.GetType().GetProperties();
            foreach (var property in properties)
            {
                var typeAttribute = (ParamName)property.GetCustomAttribute(typeof(ParamName));
                if (typeAttribute != null)
                {
                    var value = property.GetValue(obj);

                    if (value != null)
                    {
                        request.AddParameter(typeAttribute.Name, NormalizeData(property, value));
                    }
                }
            }
            return request;
        }

        /// <summary>
        /// Приведение обьекта к валиднаму для обработки на основе PropertyInfo
        /// </summary>
        /// <param name="property">Информация о свойстве</param>
        /// <param name="value">Значение</param>
        /// <returns>Нормализованое значение</returns>
        private static object NormalizeData(PropertyInfo property, object value)
        {
            if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
            {
                if ((bool)value)
                {
                    return 1;
                }

                return 0;
            }

            if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            {
                var date = ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss");
                return date;
            }

            if (property.PropertyType.BaseType == typeof(Enum))
            {
                return (int)value;
            }

            return value;
        }

        /// <summary>
        /// Выполнение запроса с использованием полити запросов Policy
        /// </summary>
        /// <typeparam name="T">Тип обьекта для десериализации обьекта ответа</typeparam>
        /// <param name="client">Клиент для отправки запроса</param>
        /// <param name="request">Сформиованый запрос для отправки на сервер</param>
        /// <param name="policy">Политика обработки ответа и повторения запроса</param>
        /// <returns>Полученый ответ с десериализованым обьектом</returns>
        public static IRestResponse<T> ExecuteWithPolicy<T>(this IRestClient client, IRestRequest request, Policy<IRestResponse<T>> policy)
        {
            var val = policy.ExecuteAndCapture(() => client.Execute<T>(request));

            return val.Result ?? new RestResponse<T> { Request = request, ErrorException = val.FinalException };
        }

        /// <summary>
        /// Выполнение запроса с использованием полити запросов Policy
        /// </summary>
        /// <param name="client">Клиент для отправки запроса</param>
        /// <param name="request">Сформиованый запрос для отправки на сервер</param>
        /// <param name="policy">Политика обработки ответа и повторения запроса</param>
        /// <returns>Ответ сервера</returns>
        public static IRestResponse ExecuteWithPolicy(this IRestClient client, IRestRequest request, Policy<IRestResponse> policy)
        {
            // capture the exception so we can push it though the standard response flow.
            var val = policy.ExecuteAndCapture(() => client.Execute(request));

            var rr = val.Result;

            if (rr == null)
            {
                rr = new RestResponse
                {
                    Request = request,
                    ErrorException = val.FinalException
                };
            }

            return rr;
        }

        public static IRestResponse ExecuteWitHeaders(this IRestClient client, IRestRequest request, Policy<IRestResponse> policy)
        {
            client.CookieContainer = new CookieContainer();
            client.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            request.AddHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            request.AddHeader("Accept-Language", "ru-RU,ru;q=0.8,en-US;q=0.5,en;q=0.3");
            request.AddHeader("Connection", "keep-alive");
            request.AddHeader("Accept-Encoding", "gzip");
            request.AddHeader("TE", "Trailers");

            var resp = client.ExecuteWithPolicy(request, policy);

            if (resp.StatusCode == 0)
            {
                request.AddHeader("X-Requested-With", "XMLHttpRequest");
                resp = client.ExecuteWithPolicy(request, policy);
            }

            return resp;
        }

        public static IRestResponse ExecuteWithRedirects(this IRestClient client, IRestRequest request, Policy<IRestResponse> policy) => ExecuteWithRedirects(client, request, policy, false);
        public static IRestResponse ExecuteWithRedirects(this IRestClient client, IRestRequest request, Policy<IRestResponse> policy, bool ignoreNextRedirects)
        {
            string lastLocation = null;
            while (true)
            {
                var resp = client.ExecuteWitHeaders(request, policy);
                resp.Content = null;

                var location = GetUrlFromResp(resp);
                var isCreated = Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out var newLocation);

                if (isCreated && lastLocation != location)
                {
                    lastLocation = newLocation.AbsoluteUri;
                    request = new RestRequest(newLocation);
                }
                else if (resp.ResponseUri != null && !ignoreNextRedirects)
                {
                    var redirect = ExecuteWithRedirects(client, new RestRequest(resp.ResponseUri), policy, true);
                    if (redirect?.ResponseUri?.Host == resp?.ResponseUri?.Host)
                        return redirect;
                }
                else
                    return resp;
            }
        }

        private static string GetUrlFromResp(IRestResponse response)
        {
            if (response == null || response.ResponseUri == null)
                return null;

            var location = response.Headers.FirstOrDefault(x => x.Name == "Location")?.Value?.ToString();
            if (!string.IsNullOrEmpty(location))
                return location;

            var urlExpr = "(https?:\\/\\/(?:www\\.|(?!www))[a-zA-Z0-9][a-zA-Z0-9-]+[a-zA-Z0-9]\\.[^\\s]{2,}|www\\.[a-zA-Z0-9][a-zA-Z0-9-]+[a-zA-Z0-9]\\.[^\\s]{2,}|https?:\\/\\/(?:www\\.|(?!www))[a-zA-Z0-9]+\\.[^\\s]{2,}|www\\.[a-zA-Z0-9]+\\.[^\\s]{2,})";
            var decoded = HttpUtility.ParseQueryString(response.ResponseUri.AbsoluteUri);
            var url = decoded.AllKeys.Select(x => decoded[x]).FirstOrDefault(x => Regex.IsMatch(x, urlExpr));

            if (!string.IsNullOrEmpty(url))
                return Regex.Match(url, urlExpr)?.Value;

            return null;
        }
    }

}
