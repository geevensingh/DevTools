using Microsoft.VisualStudio.TestTools.UnitTesting;
using Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Json.Tests
{
    [TestClass()]
    public class JsonObjectFactoryTests
    {
        [TestMethod()]
        [DeploymentItem(@"..\JsonViewer\Examples Json\FlatList.json")]
        public async void TryDeserializeTest()
        {
            string json = File.ReadAllText(@"FlatList.json");
            DeserializeResult result = await JsonObjectFactory.TrySimpleDeserialize(json);
            Assert.IsTrue(result.IsSuccessful());
            Assert.IsFalse(result.HasExtraText);
            Assert.AreEqual(60, result.Dictionary.Keys.Count);
        }
    }
}