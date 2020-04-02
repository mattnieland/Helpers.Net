using Helpers.Net.Objects;
using System;

namespace Helpers.Net.Extensions
{
	public static class SharpObjectExtensions
	{
		public static SharpObject AsSharpObject(this object obj)
		{
			return SharpObject.Copy(obj).Clone();			
		}

		//public static Dictionary<string, object> AsDictionary(this object obj)
		//{
		//	return SharpObject.Copy(obj).Clone().ToDictionary();
		//}
		public static T ToObject<T>(this SharpObject obj)
		{
			var instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { });

			foreach (var field in instance.GetType().GetProperties())
			{
				var fieldName = field.Name.CamelCase();
				field.SetValue(instance, obj[fieldName]);
			}
			return (T)instance;
		}
	}
}
