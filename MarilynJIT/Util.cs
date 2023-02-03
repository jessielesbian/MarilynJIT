using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MarilynJIT.Util
{
	public static class RegisteredObjectsManager<T>
	{
		private static readonly ConcurrentDictionary<ulong, T> keyValuePairs = new ConcurrentDictionary<ulong, T>();
		public static ulong Add(T value){
			Span<ulong> span = stackalloc ulong[1];
			Span<byte> bytes = MemoryMarshal.AsBytes(span);
			while(true){
				RandomNumberGenerator.Fill(bytes);
				ulong id = span[0];
				if (keyValuePairs.TryAdd(id, value)){
					return id;
				}
			}
		}
		public static T Get(ulong id){
			return keyValuePairs[id];
		}
		public static void Free(ulong id){
			if(keyValuePairs.TryRemove(id, out _)){
				return;
			}
			throw new NullReferenceException("Free non-existant registered object");
		}
		public static readonly MethodInfo get = typeof(RegisteredObjectsManager<T>).GetMethod("Get");
	}
}
