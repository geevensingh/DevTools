﻿using System;
using Utilities;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Utilities_Tests
{
    [TestClass]
    public class StringHelperTest
    {
        private string[] MakeStringArray(uint count, string prefix)
        {
            List<string> result = new List<string>();
            for (uint ii = 0; ii < count; ii++)
            {
                result.Add(prefix + "-" + ii.ToString());
            }
            return result.ToArray();
        }

        private void CompareArrays<T>(T[] first, T[] second)
        {
            Assert.AreEqual(first.Length, second.Length);
            for (int ii = 0; ii < first.Length; ii++)
            {
                Assert.AreEqual(first[ii], second[ii]);
            }
        }

        [TestMethod]
        public void TestToLower()
        {
            CompareArrays(MakeStringArray(10, "lower-case"), StringExtensions.ToLower(MakeStringArray(10, "lower-case")));
            CompareArrays(MakeStringArray(10, "lower-case"), StringExtensions.ToLower(MakeStringArray(10, "LOWER-CASE")));
        }

        [TestMethod]
        public void TestTrimStart()
        {
            Assert.AreEqual("foo", StringExtensions.TrimStart("foo", "bar"));
            Assert.AreEqual("bar", StringExtensions.TrimStart("foobar", "foo"));
            Assert.AreEqual("foobar", StringExtensions.TrimStart("foobar", "Foo"));
            Assert.AreEqual("bar", StringExtensions.TrimStart("foobar", "Foo", StringComparison.CurrentCultureIgnoreCase));
            Assert.AreEqual("BAR", StringExtensions.TrimStart("FOOBAR", "Foo", StringComparison.CurrentCultureIgnoreCase));
        }

        [TestMethod]
        public void TestAnyLine()
        {
            string[] lines = { "abcdefg", "hijklmn", "foobar", "baz" };
            Assert.IsTrue(StringExtensions.AnyLineContains(lines, "a"));
            Assert.IsTrue(StringExtensions.AnyLineContains(lines, "b"));
            Assert.IsFalse(StringExtensions.AnyLineContains(lines, "w"));
            Assert.IsFalse(StringExtensions.AnyLineContains(lines, "x"));

            Assert.IsTrue(StringExtensions.AnyLineIs(lines, "baz"));
            Assert.IsTrue(StringExtensions.AnyLineIs(lines, "foobar"));
            Assert.IsFalse(StringExtensions.AnyLineIs(lines, "Baz"));
            Assert.IsFalse(StringExtensions.AnyLineIs(lines, "abcd"));
        }
    }
}
