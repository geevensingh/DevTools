namespace Json.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    [TestClass()]
    public class JsonObjectTests
    {
        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\UnPretty.json")]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\LargeTest.json")]
        public async Task PrettyValueString()
        {
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(File.ReadAllText(@"UnPretty.json"));
            RootObject rootObject = await RootObject.Create(result.GetEverythingDictionary());
            Assert.AreEqual(File.ReadAllText(@"LargeTest.json"), rootObject.PrettyValueString);
        }


        //[TestMethod()]
        //public void JsonObjectTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void IsParsableJsonStringTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void SetChildrenTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void CountAtDepthTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void HasLevelTest()
        //{
        //    Assert.Fail();
        //}

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Xpert - Response.json")]
        public async Task TreatAsJsonTestAsync()
        {
            DeserializeResult result = await JsonObjectFactory.TryAgressiveDeserialize(File.ReadAllText(@"Xpert - Response.json"));
            RootObject rootObject = await RootObject.Create(result.GetEverythingDictionary());

            await TestJsonConvert(rootObject, @"data\RequestDetails", 2, 10, 40, "POST https://dunning.prod.sd.net/e381d280-b3b4-4aac-9cbb-9429f854eabb/capture-schedules/6fd7dc232199c5bdeff537aede5f2e05/adjust\r\nConnection: Keep-Alive\r\nAccept: application/json\r\nExpect: 100-continue\r\nHost: dunning.prod.sd.net\r\nUser-Agent: M$Purchase\r\napi-version: 2016-06-30\r\nx-ms-tracking-id: 78b933da-b18a-48f6-8072-c43af224d22f\r\nx-ms-correlation-id: 78b933da-b18a-48f6-8072-c43af224d22f\r\nMS-CV: eeJA31RTBk+WweC9.45.37.63.9.4.13\r\nContent-Length: 271\r\nContent-Type: application/json; charset=utf-8\r\n\r\n{\"adjustments\":[{\"capture_instructions\":{\"payment_instrument_id\":{\"account_id\":\"e381d280-b3b4-4aac-9cbb-9429f854eabb\",\"id\":\"TDEaSAAAAAAGAACA\"},\"instruction_type\":\"single_payment_instrument\"},\"type\":\"capture_instructions_adjustment\",\"reason\":\"payment_instrument_update\"}]}");
            await TestJsonConvert(rootObject, @"data\ResponseDetails", 3, 3, 40, "500 InternalServerError\r\nx-ms-request-id: 78b933da-b18a-48f6-8072-c43af224d22f\r\nContent-Type: application/json; charset=utf-8\r\n\r\n{\"code\":\"InternalServerError\",\"message\":\"An unexpected error was encountered: System.Threading.Tasks.TaskCanceledException: A task was canceled.\\r\\n   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)\\r\\n   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)\\r\\n   at Microsoft.Commerce.Billing.Web.BillingHttpClient.<Send>d__10.MoveNext()\\r\\n--- End of stack trace from previous location where exception was thrown ---\\r\\n   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()\\r\\n   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)\\r\\n   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)\\r\\n   at System.Runtime.CompilerServices.TaskAwaiter.GetResult()\\r\\n   at Microsoft.Commerce.Billing.Web.BillingHttpClient.<Send>d__7.MoveNext()\\r\\n--- End of stack trace from previous location where exception was thrown ---\\r\\n   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()\\r\\n   at System.Runtime.CompilerServ\"}");
        }

        private static async Task TestJsonConvert(RootObject rootObject, string jsonPath, int newChildCount, int newTotalChildCount, int oldRootTotalCount, string originalValue)
        {
            Assert.AreEqual(oldRootTotalCount, rootObject.TotalChildCount);
            Assert.AreEqual(rootObject.TotalChildCount, rootObject.AllChildren.Count);

            // Find the right object
            JsonObject jsonObject = rootObject.GetChild(jsonPath);
            Assert.AreEqual(jsonPath, jsonObject.Path);

            // Verify it has no children
            Assert.IsFalse(jsonObject.HasChildren);
            Assert.AreEqual(0, jsonObject.Children.Count);
            Assert.AreEqual(0, jsonObject.TotalChildCount);
            Assert.AreEqual(jsonObject.TotalChildCount, jsonObject.AllChildren.Count);

            // Verify it can be converted
            Assert.IsTrue(await jsonObject.IsParsableJsonString());
            Assert.IsTrue(await jsonObject.CanTreatAsJson());
            Assert.IsFalse(jsonObject.CanTreatAsText);

            // Save off the original data
            Assert.AreEqual(originalValue, jsonObject.Value);
            string originalValueString = jsonObject.ValueString;
            object originalTypedValue = jsonObject.TypedValue;
            string originalValueTypeString = jsonObject.ValueTypeString;
            Assert.AreEqual(originalValue, originalValueString);
            Assert.AreEqual(originalValue, originalTypedValue);
            Assert.AreEqual("parse-able-string", originalValueTypeString);

            // Convert it
            await jsonObject.TreatAsJson();

            // Verify it now has children
            Assert.IsTrue(jsonObject.HasChildren);
            Assert.AreEqual(newChildCount, jsonObject.Children.Count);
            Assert.AreEqual(newTotalChildCount, jsonObject.TotalChildCount);
            Assert.AreEqual(jsonObject.TotalChildCount, jsonObject.AllChildren.Count);
            Assert.AreEqual(oldRootTotalCount + jsonObject.TotalChildCount, rootObject.TotalChildCount);
            Assert.AreEqual(rootObject.TotalChildCount, rootObject.AllChildren.Count);

            // Verify its new data:
            Dictionary<string, object> value = (Dictionary<string, object>)jsonObject.Value;
            Assert.IsNotNull(jsonObject.Value as Dictionary<string, object>);
            Assert.IsNotNull(jsonObject.Value as Dictionary<string, object>);
            if (newChildCount == newTotalChildCount)
            {
                Assert.AreEqual("json-object{" + newChildCount + "}", jsonObject.ValueTypeString);
            }
            else
            {
                Assert.AreEqual("json-object{" + newChildCount + "} (tree: " + newTotalChildCount + ")", jsonObject.ValueTypeString);
            }

            // Verify it can be converted back
            Assert.IsFalse(await jsonObject.IsParsableJsonString());
            Assert.IsFalse(await jsonObject.CanTreatAsJson());
            Assert.IsTrue(jsonObject.CanTreatAsText);

            // Convert it back
            jsonObject.TreatAsText();

            // Verify it no longer has children (again)
            Assert.IsFalse(jsonObject.HasChildren);
            Assert.AreEqual(0, jsonObject.Children.Count);
            Assert.AreEqual(0, jsonObject.TotalChildCount);
            Assert.AreEqual(jsonObject.TotalChildCount, jsonObject.AllChildren.Count);
            Assert.AreEqual(oldRootTotalCount, rootObject.TotalChildCount);
            Assert.AreEqual(rootObject.TotalChildCount, rootObject.AllChildren.Count);

            // Verify it can be converted (again)
            Assert.IsTrue(await jsonObject.IsParsableJsonString());
            Assert.IsTrue(await jsonObject.CanTreatAsJson());
            Assert.IsFalse(jsonObject.CanTreatAsText);

            // Verify it is the same as the original
            Assert.AreEqual(originalValue, jsonObject.Value);
            Assert.AreEqual(originalValueString, jsonObject.ValueString);
            Assert.AreEqual(originalTypedValue, jsonObject.TypedValue);
            Assert.AreEqual(originalValueTypeString, jsonObject.ValueTypeString);
        }

        //[TestMethod()]
        //public void FlushRulesTest()
        //{
        //    Assert.Fail();
        //}
    }
}