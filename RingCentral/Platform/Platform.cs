﻿using RingCentral.Http;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RingCentral
{
    public class AuthEventArgs : EventArgs
    {
        public object Response { get; private set; }
        public AuthEventArgs(ApiResponse response)
        {
            Response = response;
        }
    }

    public class Platform
    {
        public event EventHandler<AuthEventArgs> AuthDataRefreshed;

        private const string AccessTokenTtl = "3600"; // 60 minutes
        private const string RefreshTokenTtl = "36000"; // 10 hours
        private const string RefreshTokenTtlRemember = "604800"; // 1 week
        private const string TokenEndpoint = "restapi/oauth/token";
        private const string RevokeEndpoint = "restapi/oauth/revoke";

        public HttpClient _client { private get; set; }
        public Auth Auth { get; private set; }

        public Platform(string appKey, string appSecret, string serverUrl, string appName = "", string appVersion = "")
        {
            this.appKey = appKey;
            this.appSecret = appSecret;
            this.ServerUrl = serverUrl;
            Auth = new Auth();
            _client = new HttpClient { BaseAddress = new Uri(this.ServerUrl) };
            SetUserAgentHeader(appName, appVersion);
        }

        private string appKey;
        private string appSecret;
        public string ServerUrl { get; private set; }

        /// <summary>
        ///     Method to generate Access Token to establish an authenticated session
        /// </summary>
        /// <param name="username">Username of RingCentral user</param>
        /// <param name="extension">Optional: Extension number to login</param>
        /// <param name="password">Password of the RingCentral User</param>
        /// <param name="remember">If set to true, refresh token TTL will be one week, otherwise it's 10 hours</param>
        /// <returns>apiResponse of Authenticate result.</returns>
        public ApiResponse Login(string username, string extension, string password, bool remember)
        {
            var body = new Dictionary<string, string>
                       {
                           {"username", username},
                           {"password", password},
                           {"extension", extension},
                           {"grant_type", "password"},
                           {"access_token_ttl", AccessTokenTtl},
                           {"refresh_token_ttl", remember ? RefreshTokenTtlRemember : RefreshTokenTtl}
                       };

            var request = new Request(TokenEndpoint, body);
            var result = AuthCall(request);

            Auth.Remember = remember;
            Auth.SetData(result.Json);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Auth.AccessToken);

            return result;
        }

        /// <summary>
        ///     Refreshes expired Access token during valid lifetime of Refresh Token
        /// </summary>
        /// <returns>string response of Refresh result</returns>
        public ApiResponse Refresh()
        {
            if (!Auth.IsRefreshTokenValid()) throw new Exception("Refresh Token has Expired");

            var body = new Dictionary<string, string>
                       {
                           {"grant_type", "refresh_token"},
                           {"refresh_token", Auth.RefreshToken},
                           {"access_token_ttl", AccessTokenTtl},
                           {"refresh_token_ttl", Auth.Remember ? RefreshTokenTtlRemember : RefreshTokenTtl}
                       };

            var request = new Request(TokenEndpoint, body);
            var result = AuthCall(request);

            Auth.SetData(result.Json);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Auth.AccessToken);

            return result;
        }

        /// <summary>
        ///     Revokes the already granted access to stop application activity
        /// </summary>
        /// <returns>string response of Revoke result</returns>
        public ApiResponse Logout()
        {
            var body = new Dictionary<string, string> { { "token", Auth.AccessToken } };
            Auth.Reset();
            var request = new Request(RevokeEndpoint, body);
            return AuthCall(request);
        }

        /// <summary>
        ///     Authentication, Refresh and Revoke requests all require an Authentication Header Value of "Basic".  This is a
        ///     special method to handle those requests.
        /// </summary>
        /// <param name="request">
        ///     A Request object with a url and a dictionary of key value pairs (<c>Authenticate</c>,
        ///     <c>Refresh</c>, <c>Revoke</c>)
        /// </param>
        /// <returns>Response object</returns>
        private ApiResponse AuthCall(Request request)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GenerateAuthToken());
            var response = _client.PostAsync(request.Url, request.HttpContent).Result;
            var result = new ApiResponse(response);
            AuthDataRefreshed?.Invoke(request, new AuthEventArgs(result));
            return result;
        }

        public ApiResponse Get(Request request)
        {
            return GetAsync(request).Result;
        }

        public async Task<ApiResponse> GetAsync(Request request)
        {
            return await SendAsync(HttpMethod.Get, request);
        }

        public ApiResponse Post(Request request)
        {
            return PostAsync(request).Result;
        }

        public async Task<ApiResponse> PostAsync(Request request)
        {
            return await SendAsync(HttpMethod.Post, request);
        }

        public ApiResponse Delete(Request request)
        {
            return DeleteAsync(request).Result;
        }

        public async Task<ApiResponse> DeleteAsync(Request request)
        {
            return await SendAsync(HttpMethod.Delete, request);
        }


        public ApiResponse Put(Request request)
        {
            return PutAsync(request).Result;
        }

        public async Task<ApiResponse> PutAsync(Request request)
        {
            return await SendAsync(HttpMethod.Put, request);
        }


        public ApiResponse Send(HttpMethod httpMethod, Request request)
        {
            return SendAsync(httpMethod, request).Result;
        }

        public async Task<ApiResponse> SendAsync(HttpMethod httpMethod, Request request)
        {
            if (!LoggedIn())
            {
                throw new Exception("Access has Expired");
            }

            var requestMessage = new HttpRequestMessage();
            requestMessage.Content = request.HttpContent;
            requestMessage.Method = httpMethod;
            requestMessage.RequestUri = request.Uri;
            if (request.HttpMethodTunneling)
            {
                requestMessage.ApplyHttpMethodTunneling();
            }

            return new ApiResponse(await _client.SendAsync(requestMessage), requestMessage);
        }



        /// <summary>
        /// Generates auth token by encoding appKey and appSecret then converting it to base64
        /// </summary>
        /// <returns>The Api Key</returns>
        private string GenerateAuthToken()
        {
            var byteArray = Encoding.UTF8.GetBytes(appKey + ":" + appSecret);
            return Convert.ToBase64String(byteArray);
        }

        /// <summary>
        ///     You also may supply custom AppName:AppVersion in the form of a header with your application codename and version. These parameters
        ///     are optional but they will help a lot to identify your application in API logs and speed up any potential troubleshooting.
        ///     Allowed characters for AppName:AppVersion are- letters, digits, hyphen, dot and underscore.
        /// </summary>
        /// <param name="appName">Application Name</param>
        /// <param name="appVersion">Application Version</param>
        private void SetUserAgentHeader(string appName, string appVersion)
        {
            var agentString = String.Empty;

            #region Set UA String
            if (!string.IsNullOrEmpty(appName))
            {
                agentString += appName;
                if (!string.IsNullOrEmpty(appVersion))
                {
                    agentString += "_" + appVersion;
                }
            }
            if (string.IsNullOrEmpty(agentString))
            {
                agentString += "RCCSSDK_" + SDK.Version;
            }
            else
            {
                agentString += ".RCCSSDK_" + SDK.Version;
            }
            #endregion

            Regex r = new Regex("(?:[^a-z0-9-_. ]|(?<=['\"])s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var ua = r.Replace(agentString, String.Empty);

            _client.DefaultRequestHeaders.Add("User-Agent", ua);
            _client.DefaultRequestHeaders.Add("RC-User-Agent", ua);
        }

        private object refreshLock = new object();
        /// <summary>
        ///     Determines if Access is valid and returns the boolean result.  If access is not valid but refresh token is valid
        ///     then a refresh is issued.
        /// </summary>
        /// <returns>boolean value of access authorization</returns>
        public bool LoggedIn()
        {
            if (Auth.IsAccessTokenValid())
            {
                return true;
            }

            if (Auth.IsRefreshTokenValid())
            {
                //obtain a mutual-exclusion lock for the thisLock object, execute statement and then release the lock.
                lock (refreshLock)
                {
                    Refresh();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// When your application needs to authentiate an user, redirect the user to RingCentral API server for authentication.
        /// This method helps you to build the URI. Later you can redirect user to this URI.
        /// </summary>
        /// <param name="redirectUri">This is a callback URI which determines where the response will be sent to. The value of this parameter must exactly match one of the URIs you have provided for your app upon registration. This URI can be HTTP/HTTPS address for web applications or custom scheme URI for mobile or desktop applications.</param>
        /// <param name="state">Optional, recommended. An opaque value used by the client to maintain state between the request and callback. The authorization server includes this value when redirecting the user-agent back to the client. The parameter should be used for preventing cross-site request forgery</param>
        /// <returns></returns>
        public string AuthorizeUri(string redirectUri, string state = "")
        {
            var baseUrl = ServerUrl + "/restapi/oauth/authorize";
            var authUrl = string.Format("{0}?response_type=code&state={1}&redirect_uri={2}&client_id={3}",
                baseUrl, Uri.EscapeDataString(state),
                Uri.EscapeDataString(redirectUri),
                Uri.EscapeDataString(appKey));
            return authUrl;
        }

        /// <summary>
        /// Do authentication with the authorization code returned from server
        /// </summary>
        /// <param name="authCode">The authorization code returned from server</param>
        /// <param name="redirectUri">The same redirectUri when you were obtaining the authCode in previous step</param>
        /// <returns></returns>
        public ApiResponse Authenticate(string authCode, string redirectUri)
        {
            var request = new Request("/restapi/oauth/token",
                new Dictionary<string, string> { { "grant_type", "authorization_code" },
                    { "redirect_uri", redirectUri }, { "code", authCode } });
            var response = AuthCall(request);
            Auth.SetData(response.Json);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Auth.AccessToken);
            return response;
        }
    }
}
