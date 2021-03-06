namespace Microsoft.ApplicationInsights.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Http;

    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.Common;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.DependencyCollector;
    using Microsoft.ApplicationInsights.DependencyCollector.Implementation;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.TestFramework;
    using Microsoft.ApplicationInsights.Web.TestFramework;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for DependencyCollectorDiagnosticListener.
    /// </summary>
    [TestClass]
    public partial class DependencyCollectorDiagnosticListenerTests
    {
        private const string RequestUrl = "www.example.com";
        private const string RequestUrlWithScheme = "https://" + RequestUrl;
        private const string HttpOkResultCode = "200";
        private const string NotFoundResultCode = "404";

        private List<ITelemetry> sentTelemetry;
        private object request;
        private object response;
        private object responseHeaders;

        private TelemetryConfiguration configuration;
        private string testInstrumentationKey1 = nameof(testInstrumentationKey1);
        private string testApplicationId1 = "cid-v1:" + nameof(testApplicationId1);
        private string testApplicationId2 = "cid-v1:" + nameof(testApplicationId2);
        private StubTelemetryChannel telemetryChannel;
        private HttpCoreDiagnosticSourceListener listener;

        /// <summary>
        /// Initialize function that gets called once before any tests in this class are run.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.sentTelemetry = new List<ITelemetry>();
            this.request = null;
            this.response = null;
            this.responseHeaders = null;

            this.telemetryChannel = new StubTelemetryChannel()
            {
                EndpointAddress = "https://endpointaddress",
                OnSend = telemetry =>
                {
                    this.sentTelemetry.Add(telemetry);

                    // The correlation id lookup service also makes http call, just make sure we skip that
                    DependencyTelemetry depTelemetry = telemetry as DependencyTelemetry;
                    if (depTelemetry != null)
                    {
                        depTelemetry.TryGetOperationDetail(RemoteDependencyConstants.HttpRequestOperationDetailName, out this.request);
                        depTelemetry.TryGetOperationDetail(RemoteDependencyConstants.HttpResponseOperationDetailName, out this.response);
                        depTelemetry.TryGetOperationDetail(RemoteDependencyConstants.HttpResponseHeadersOperationDetailName, out this.responseHeaders);
                    }
                },
            };

            this.testInstrumentationKey1 = Guid.NewGuid().ToString();

            this.configuration = new TelemetryConfiguration
            {
                TelemetryChannel = this.telemetryChannel,
                InstrumentationKey = this.testInstrumentationKey1,
                ApplicationIdProvider = new MockApplicationIdProvider(this.testInstrumentationKey1, this.testApplicationId1)
            };

            this.configuration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
            this.listener = new HttpCoreDiagnosticSourceListener(
                this.configuration,
                setComponentCorrelationHttpHeaders: true,
                correlationDomainExclusionList: new string[] { "excluded.host.com" },
                injectLegacyHeaders: false,
                injectW3CHeaders: false);
        }

        /// <summary>
        /// Cleans up.
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            while (Activity.Current != null)
            {
                Activity.Current.Stop();
            }

            this.listener?.Dispose();
        }

        /// <summary>
        /// Call OnRequest() with no uri in the HttpRequestMessage.
        /// </summary>
        [TestMethod]
        public void OnRequestWithRequestEventWithNoRequestUri()
        {
            var request = new HttpRequestMessage();

            this.listener.OnRequest(request, Guid.NewGuid());

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);
        }

        /// <summary>
        /// Very second request does not throw an exception.
        /// </summary>
        [TestMethod]
        public void VerifyOnRequestWithSameDoesNotThrowException()
        {
            Guid loggingRequestId = Guid.NewGuid();

            var request = new HttpRequestMessage(HttpMethod.Get, RequestUrlWithScheme);

            this.listener.OnRequest(request, loggingRequestId);
            this.listener.OnRequest(request, loggingRequestId);
        }

        /// <summary>
        /// Scenario: Assume a retry request after first request fails.
        /// Very second request does not throw an exception.
        /// Verify that telemetry is collected for both requests.
        /// </summary>
        /// <remarks>
        /// IGNORE
        /// THE FOLLOWING ASSERTS ARE EXPECTED TO PASS, BUT CURRENTLY FAIL DUE TO A KNOWN ISSUE: #724 
        /// Because two identical requests were sent, whichever completes first will remove the request from pending telemetry.
        /// </remarks>
        [Ignore]
        [TestMethod]
        public void VerifyOnRequestWithDuplicateRequestCreatesValidTelemetry()
        {
            Guid loggingRequestId = Guid.NewGuid();

            var request = new HttpRequestMessage(HttpMethod.Get, RequestUrlWithScheme);

            // first request, expected to fail
            this.listener.OnRequest(request, loggingRequestId);

            // second request (BEFORE HANDLING FIRST RESPONSE), expected to pass 
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            // first request fails
            HttpResponseMessage response1 = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            };

            this.listener.OnResponse(response1, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreEqual(false, ((DependencyTelemetry)this.sentTelemetry.Last()).Success); // first request fails

            Assert.Inconclusive();

            // THE FOLLOWING ASSERTS ARE EXPECTED TO PASS, BUT CURRENTLY FAIL DUE TO A KNOWN ISSUE: #724 
            // Because two identical requests were sent, whichever completes first will remove the request from pending telemetry.

            // second request is success
            HttpResponseMessage response2 = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            this.listener.OnResponse(response2, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(2, this.sentTelemetry.Count);
            Assert.AreEqual(true, ((DependencyTelemetry)this.sentTelemetry.Last()).Success); // second request is success
        }

        /// <summary>
        /// Call OnRequest() with uri that is in the excluded domain list.
        /// </summary>
        [TestMethod]
        public void OnRequestWithUriInExcludedDomainList()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://excluded.host.com/path/to/file.html");
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            Assert.IsNull(HttpHeadersUtilities.GetRequestContextKeyValue(request.Headers, RequestResponseHeaders.RequestContextCorrelationSourceKey));
            Assert.IsNull(HttpHeadersUtilities.GetRequestContextKeyValue(request.Headers, RequestResponseHeaders.RequestIdHeader));
            Assert.IsNull(HttpHeadersUtilities.GetRequestContextKeyValue(request.Headers, RequestResponseHeaders.StandardParentIdHeader));
            Assert.IsNull(HttpHeadersUtilities.GetRequestContextKeyValue(request.Headers, RequestResponseHeaders.StandardRootIdHeader));
        }

        /// <summary>
        /// OnRequest() injects legacy headers when configured to do so.
        /// </summary>
        [TestMethod]
        public void OnRequestInjectsLegacyHeaders()
        {
            var listenerWithLegacyHeaders = new HttpCoreDiagnosticSourceListener(
                this.configuration,
                setComponentCorrelationHttpHeaders: true,
                correlationDomainExclusionList: new[] { "excluded.host.com" },
                injectLegacyHeaders: true,
                injectW3CHeaders: false);

            using (listenerWithLegacyHeaders)
            {
                Guid loggingRequestId = Guid.NewGuid();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
                listenerWithLegacyHeaders.OnRequest(request, loggingRequestId);

                IOperationHolder<DependencyTelemetry> dependency;
                Assert.IsTrue(
                    listenerWithLegacyHeaders.PendingDependencyTelemetry.TryGetValue(request, out dependency));
                Assert.AreEqual(0, this.sentTelemetry.Count);

                var legacyRootIdHeader = GetRequestHeaderValues(request, RequestResponseHeaders.StandardRootIdHeader)
                    .Single();
                var legacyParentIdHeader =
                    GetRequestHeaderValues(request, RequestResponseHeaders.StandardParentIdHeader).Single();
                var requestIdHeader = GetRequestHeaderValues(request, RequestResponseHeaders.RequestIdHeader).Single();
                Assert.AreEqual(dependency.Telemetry.Id, legacyParentIdHeader);
                Assert.AreEqual(dependency.Telemetry.Context.Operation.Id, legacyRootIdHeader);
                Assert.AreEqual(dependency.Telemetry.Id, requestIdHeader);
            }
        }

        /// <summary>
        /// OnRequest() does not inject legacy headers when configured to do so.
        /// </summary>
        [TestMethod]
        public void OnRequestDoesNotInjectLegacyHeaders()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            var requestIdHeader = GetRequestHeaderValues(request, RequestResponseHeaders.RequestIdHeader).Single();
            Assert.IsFalse(request.Headers.Contains(RequestResponseHeaders.StandardRootIdHeader));
            Assert.IsFalse(request.Headers.Contains(RequestResponseHeaders.StandardParentIdHeader));
            Assert.AreEqual(dependency.Telemetry.Id, requestIdHeader);
        }

        /// <summary>
        /// Call OnRequest() with valid arguments.
        /// </summary>
        [TestMethod]
        public void OnRequestWithRequestEvent()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual("POST /", telemetry.Name);
            Assert.AreEqual(RequestUrl, telemetry.Target);
            Assert.AreEqual(RemoteDependencyConstants.HTTP, telemetry.Type);
            Assert.AreEqual(RequestUrlWithScheme, telemetry.Data);
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            Assert.AreEqual(this.testApplicationId1, GetRequestContextKeyValue(request, RequestResponseHeaders.RequestContextCorrelationSourceKey));

            var requestIdHeader = GetRequestHeaderValues(request, RequestResponseHeaders.RequestIdHeader).Single();
            Assert.IsFalse(string.IsNullOrEmpty(requestIdHeader));
            Assert.AreEqual(0, this.sentTelemetry.Count);
        }

        /// <summary>
        /// Call OnResponse() when no matching OnRequest() call has been made.
        /// </summary>
        [TestMethod]
        public void OnResponseWithResponseEventButNoMatchingRequest()
        {
            var response = new HttpResponseMessage
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, RequestUrlWithScheme)
            };

            this.listener.OnResponse(response, Guid.NewGuid());
            Assert.AreEqual(0, this.sentTelemetry.Count);
        }

        /// <summary>
        /// Call OnResponse() with a successful request but no target instrumentation key in the response headers.
        /// </summary>
        [TestMethod]
        public void OnResponseWithSuccessfulResponseEventWithMatchingRequestAndNoTargetInstrumentationKeyHasHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));

            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.HTTP, telemetry.Type);
            Assert.AreEqual(RequestUrl, telemetry.Target);
            Assert.AreEqual(HttpOkResultCode, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            string expectedVersion =
                SdkVersionHelper.GetExpectedSdkVersion(typeof(DependencyTrackingTelemetryModule), prefix: "rdddsc:");
            Assert.AreEqual(expectedVersion, telemetry.Context.GetInternalContext().SdkVersion);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Call OnResponse() with a not found request result but no target instrumentation key in the response headers.
        /// </summary>
        [TestMethod]
        public void OnResponseWithNotFoundResponseEventWithMatchingRequestAndNoTargetInstrumentationKeyHasHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));

            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            };

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.HTTP, telemetry.Type);
            Assert.AreEqual(RequestUrl, telemetry.Target);
            Assert.AreEqual(NotFoundResultCode, telemetry.ResultCode);
            Assert.AreEqual(false, telemetry.Success);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Call OnResponse() with a successful request and same target instrumentation key in the response headers as the source instrumentation key.
        /// </summary>
        [TestMethod]
        public void OnResponseWithSuccessfulResponseEventWithMatchingRequestAndSameTargetInstrumentationKeyHashHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);
            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));

            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success, "request was not successful");

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            response.Headers.Add(RequestResponseHeaders.RequestContextCorrelationTargetKey, this.testApplicationId1);

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.HTTP, telemetry.Type);
            Assert.AreEqual(RequestUrl, telemetry.Target);
            Assert.AreEqual(HttpOkResultCode, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success, "response was not successful");

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Call OnResponse() with a not found request result code and same target instrumentation key in the response headers as the source instrumentation key.
        /// </summary>
        [TestMethod]
        public void OnResponseWithFailedResponseEventWithMatchingRequestAndSameTargetInstrumentationKeyHashHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);
            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            };

            response.Headers.Add(RequestResponseHeaders.RequestContextCorrelationTargetKey, this.testApplicationId1);

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.HTTP, telemetry.Type);
            Assert.AreEqual(RequestUrl, telemetry.Target);
            Assert.AreEqual(NotFoundResultCode, telemetry.ResultCode);
            Assert.AreEqual(false, telemetry.Success);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Call OnResponse() with a successful request and different target instrumentation key in the response headers than the source instrumentation key.
        /// </summary>
        [TestMethod]
        public void OnResponseWithSuccessfulResponseEventWithMatchingRequestAndDifferentTargetInstrumentationKeyHashHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);
            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            string targetApplicationId = this.testApplicationId2;
            HttpHeadersUtilities.SetRequestContextKeyValue(response.Headers, RequestResponseHeaders.RequestContextCorrelationTargetKey, targetApplicationId);

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.AI, telemetry.Type);
            Assert.AreEqual(GetApplicationInsightsTarget(targetApplicationId), telemetry.Target);
            Assert.AreEqual(HttpOkResultCode, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Call OnResponse() with a not found request result code and different target instrumentation key in the response headers than the source instrumentation key.
        /// </summary>
        [TestMethod]
        public void OnResponseWithFailedResponseEventWithMatchingRequestAndDifferentTargetInstrumentationKeyHashHeader()
        {
            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);
            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(0, this.sentTelemetry.Count);

            DependencyTelemetry telemetry = dependency.Telemetry;
            Assert.AreEqual(string.Empty, telemetry.ResultCode);
            Assert.AreEqual(true, telemetry.Success);

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            };

            string targetApplicationId = this.testApplicationId2;
            HttpHeadersUtilities.SetRequestContextKeyValue(response.Headers, RequestResponseHeaders.RequestContextCorrelationTargetKey, targetApplicationId);

            this.listener.OnResponse(response, loggingRequestId);
            Assert.IsFalse(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));
            Assert.AreEqual(1, this.sentTelemetry.Count);
            Assert.AreSame(telemetry, this.sentTelemetry.Single());

            Assert.AreEqual(RemoteDependencyConstants.AI, telemetry.Type);
            Assert.AreEqual(GetApplicationInsightsTarget(targetApplicationId), telemetry.Target);
            Assert.AreEqual(NotFoundResultCode, telemetry.ResultCode);
            Assert.AreEqual(false, telemetry.Success);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);
        }

        /// <summary>
        /// Tests that outgoing request has proper context when done in scope of incoming request (incoming request activity).
        /// </summary>
        [TestMethod]
        public void OnResponseWithParentActivity()
        {
            var parentActivity = new Activity("incoming_request");
            parentActivity.AddBaggage("k1", "v1");
            parentActivity.AddBaggage("k2", "v2");
            parentActivity.Start();

            Guid loggingRequestId = Guid.NewGuid();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RequestUrlWithScheme);
            this.listener.OnRequest(request, loggingRequestId);
            Assert.IsNotNull(Activity.Current);

            var parentId = request.Headers.GetValues(RequestResponseHeaders.RequestIdHeader).Single();
            Assert.AreEqual(Activity.Current.Id, parentId);

            var correlationContextHeader = request.Headers.GetValues(RequestResponseHeaders.CorrelationContextHeader).ToArray();
            Assert.AreEqual(2, correlationContextHeader.Length);
            Assert.IsTrue(correlationContextHeader.Contains("k1=v1"));
            Assert.IsTrue(correlationContextHeader.Contains("k2=v2"));

            IOperationHolder<DependencyTelemetry> dependency;
            Assert.IsTrue(this.listener.PendingDependencyTelemetry.TryGetValue(request, out dependency));

            DependencyTelemetry telemetry = dependency.Telemetry;

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            this.listener.OnResponse(response, loggingRequestId);
            Assert.AreEqual(parentActivity, Activity.Current);
            Assert.AreEqual(parentId, telemetry.Id);
            Assert.AreEqual(parentActivity.RootId, telemetry.Context.Operation.Id);
            Assert.AreEqual(parentActivity.Id, telemetry.Context.Operation.ParentId);

            // Check the operation details
            this.ValidateOperationDetails(telemetry);

            parentActivity.Stop();
        }

        private static string GetApplicationInsightsTarget(string targetApplicationId)
        {
            return $"{RequestUrl} | {targetApplicationId}";
        }

        private static IEnumerable<string> GetRequestHeaderValues(HttpRequestMessage request, string headerName)
        {
            return HttpHeadersUtilities.GetHeaderValues(request.Headers, headerName);
        }

        private static string GetRequestContextKeyValue(HttpRequestMessage request, string keyName)
        {
            return HttpHeadersUtilities.GetRequestContextKeyValue(request.Headers, keyName);
        }

        private void ValidateOperationDetails(DependencyTelemetry telemetry, bool responseExpected = true)
        {
            Assert.IsNotNull(this.request, "Request was not present and expected.");
            Assert.IsNotNull(this.request as HttpRequestMessage, "Request was not the expected type.");
            Assert.IsNull(this.responseHeaders, "Response headers were present and not expected.");
            if (responseExpected)
            {
                Assert.IsNotNull(this.response, "Response was not present and expected.");
                Assert.IsNotNull(this.response as HttpResponseMessage, "Response was not the expected type.");
            }
            else
            {
                Assert.IsNull(this.response, "Response was present and not expected.");
            }
        }
    }
}