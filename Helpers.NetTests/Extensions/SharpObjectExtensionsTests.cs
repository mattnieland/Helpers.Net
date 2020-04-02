using Microsoft.VisualStudio.TestTools.UnitTesting;
using Helpers.Net.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using Helpers.Net.Objects;

namespace Helpers.Net.Extensions.Tests
{
    [TestClass()]
    public class SharpObjectExtensionsTests
    {
        [TestMethod()]
        public void AsObjectTest()
        {
            var obj = new TestObject { Test1 = 1, Test2 = "test", Test3 = true, Test4 = new TestObject2 { Test = "test" } };
            var sharpObject = obj.AsSharpObject();
            Assert.AreEqual(sharpObject.GetString("Test2"), "test");
        }

        [TestMethod()]
        public void ToObjectTest()
        {
            var sharpObject = SharpObject.Copy(new { test1 = 1, test2 = "test", test3 = true });
            var obj = sharpObject.ToObject<TestObject>();
            Assert.AreEqual(obj.Test1, 1);
        }
    }

    class TestObject
    {
        public int Test1 { get; set; }
        public string Test2 { get; set; }

        public bool Test3 { get; set; }

        public TestObject2 Test4 { get; set; }
    }

    class TestObject2
    {
        public string Test { get; set; }
    }
}