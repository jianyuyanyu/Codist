﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestProject
{
	internal class Methods : IDisposable
	{
		public Methods() {
			M("");
		}

		~Methods() {
			Dispose(false);
		}

		void Dispose(bool disposing) {}

		public void Dispose() {
			Dispose(true);
		}

		unsafe static void M(int* val, string* text, DateTime* date) { }

		public async static Task M(string? val = null) { }

		public async Task<int> M(int? val = null) { return Task.FromResult(val ?? 0).Result; }

		public static void M<T>(T val) where T : class, IDisposable, IEnumerable<T> { }

		/// <summary>generic method with 2 type params: <typeparamref name="T1"/>, <typeparamref name="T2"/>.</summary>
		/// <typeparam name="T1">type param 1, type of <paramref name="val"/></typeparam>
		/// <typeparam name="T2">type param 2, type of <paramref name="val2"/></typeparam>
		/// <param name="val">param 1</param>
		/// <param name="val2">param 2</param>
		public static void M<T1, T2>(T1 val, T2 val2 = default) where T2 : struct { }

		public Nullable<T> M<T>(T? val, T val2) where T : struct { return default; }

		public static T M<T>(T? val) where T : struct { return val.GetValueOrDefault(); }

		public String M(string val, params string[] strings) { return String.Join(val, strings); }

		public bool M<T>(in Type @in, in DateTime date, ref DateTime date2, [Name(nameof(T))] out T val) { val = default; return false; }

		public int M(DayOfWeek val = DayOfWeek.Monday) { return (int)val; }
	}
}
