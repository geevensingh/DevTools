namespace Json.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Threading.Tasks;
    using System.IO;

    [TestClass()]
    public class JsonObjectFactoryTests
    {
        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Xpert - Simple.json")]
        public async Task TryDeserializeXpertSimple()
        {
            int rootChildren = 12;
            int totalChildren = 33;
            string json = File.ReadAllText(@"Xpert - Simple.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Xpert - Response.json")]
        public async Task TryDeserializeXpertResponse()
        {
            int rootChildren = 12;
            int totalChildren = 40;
            string json = File.ReadAllText(@"Xpert - Response.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            Assert.IsNull(result);
            Assert.IsFalse(result.IsSuccessful());

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            Assert.IsNull(result);
            Assert.IsFalse(result.IsSuccessful());
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\NestedEvents.json")]
        public async Task TryDeserializeNestedEvents()
        {
            int rootChildren = 4;
            int totalChildren = 136;
            string json = File.ReadAllText(@"NestedEvents.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Bad-Config.json")]
        public async Task TryDeserializeBadConfig()
        {
            int rootChildren = 9;
            int totalChildren = 59;
            string json = File.ReadAllText(@"Bad-Config.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\616Events.json")]
        public async Task TryDeserialize616Events()
        {
            int rootChildren = 7;
            int totalChildren = 6626;
            string json = File.ReadAllText(@"616Events.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Recursive.json")]
        public async Task TryDeserializeRecursive()
        {
            int rootChildren = 11;
            int totalChildren = 23;
            string json = File.ReadAllText(@"Recursive.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Semi-Valid.json")]
        public async Task TryDeserializeSemiValid()
        {
            int rootChildren = 11;
            int totalChildren = 17;
            string json = File.ReadAllText(@"Semi-Valid.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\StressTest.json")]
        public async Task TryDeserializeStressTest()
        {
            int rootChildren = 70;
            int totalChildren = 8910;
            string json = File.ReadAllText(@"StressTest.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\TestHeader.json")]
        public async Task TryDeserializeTestHeader()
        {
            int rootChildren = 8;
            int totalChildren = 268;
            string json = File.ReadAllText(@"TestHeader.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\VeryLargeTest.json")]
        public async Task TryDeserializeVeryLarge()
        {
            int rootChildren = 35;
            int totalChildren = 4455;
            string json = File.ReadAllText(@"VeryLargeTest.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\LargeTest.json")]
        public async Task TryDeserializeLarge()
        {
            int rootChildren = 7;
            int totalChildren = 891;
            string json = File.ReadAllText(@"LargeTest.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Simple.json")]
        public async Task TryDeserializeSimple()
        {
            int rootChildren = 1;
            int totalChildren = 6;
            string json = File.ReadAllText(@"Simple.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, rootChildren, totalChildren);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\Depth.json")]
        public async Task TryDeserializeDeepList()
        {
            string json = File.ReadAllText(@"Depth.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, 1, 15);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, 1, 15);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, 1, 15);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\FlatList.json")]
        public async Task TryDeserializeFlatList()
        {
            string json = File.ReadAllText(@"FlatList.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            await VerifyValidResult(result, 60, 60);

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, 60, 60);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, 60, 60);
        }

        [TestMethod()]
        [DeploymentItem(@"..\..\..\JsonViewer\Examples Json\FlatList.json")]
        public async Task TryDeserializeQuotedFlatList()
        {
            string json = "\"" + File.ReadAllText(@"FlatList.json").Replace("\"", "\"\"") + "\"";
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            Assert.IsNull(result);
            Assert.IsFalse(result.IsSuccessful());

            result = await JsonObjectFactory.TryAgressiveDeserialize(json);
            await VerifyValidResult(result, 60, 60);

            result = await JsonObjectFactory.TryDeserialize(json);
            await VerifyValidResult(result, 60, 60);
        }


        private async Task VerifyValidResult(DeserializeResult deserializeResult, int rootChildren, int totalChildren)
        {
            Assert.IsTrue(deserializeResult.IsSuccessful());
            Assert.IsFalse(deserializeResult.HasExtraText);
            Assert.AreEqual(rootChildren, deserializeResult.Dictionary.Keys.Count);

            RootObject rootObject = await RootObject.Create(deserializeResult.GetEverythingDictionary());
            Assert.AreEqual(rootChildren, rootObject.Children.Count);
            Assert.AreEqual(totalChildren, rootObject.TotalChildCount);
            Assert.AreEqual(totalChildren, rootObject.AllChildren.Count);
            Assert.AreEqual(rootObject, rootObject.Root);
            Assert.IsFalse(await rootObject.IsParsableJsonString());
        }
    }
}