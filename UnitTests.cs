/*
 * Unit tests for PlayHooky
 */
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
using static System.Console;
using PlayHooky;

namespace UnitTests {

	//Exception thrown on an Assertion failure. Uses to differentiate actual exceptions from our test Exceptions.
	public class AssertionFailureException : Exception {
		public AssertionFailureException() : base() { }
		public AssertionFailureException(string message) : base(message) { }
	}

	//assertion convenience class (needed because we want assertion failure to throw an exception, not popup a dialog like C#'s builtin assert does
	class Assert {

		//Simple assert, throws an AssertionFailureException with the given (optional) error message on failure.
		public static void IsTrue(bool expr, string failureMessage = "Assertion Failure") {
			if (!expr) {
				throw new AssertionFailureException(failureMessage);
			}
		}

		//Generic comparison assert
		public static void Equals<T>(T test, T control) where T : IComparable<T> => IsTrue(test.CompareTo(control) == 0, test + " != " + control + " (expected)");

		//Returns true if the given Exception is thrown, or false if it is not.
		private static bool ThrewException<E>(Action f) where E : Exception {
			bool threw = false;
			try {
				f();
			} catch (E) {
				threw = true;
			} catch { }
			return threw;
		}

		//Returns true if the given Exception is thrown, or false if another Exception or none are.
		public static void ThrowsException<E>(Action f, string message = "Unexpected Exception Thrown") where E : Exception => IsTrue(ThrewException<E>(f), message);

		//Returns true if any Exception is thrown, or false if none are.
		public static void ThrowsException(Action f, string message = "Unexpected Exception Thrown") => IsTrue(ThrewException<Exception>(f), message);

		//Returns true if no Exception is thrown, or false if any exception is.
		public static void ExceptionSafe(Action f, string message = "Unexpected Exception Thrown") => IsTrue(!ThrewException<Exception>(f), message);

	}

	//Atribute denoting a method should be called as a Unit tests.
	class UnitTestAttribute : Attribute {

		//Test number (output when a test is beginning, doesn't matter what this is)
		public int Number { private set; get; }

		//Test description (output when a test is beginning, should probably describe the current test for debugging)
		public string Description { private set; get; }

		public UnitTestAttribute(int number, string description) {
			Number = number;
			Description = description;
		}

	}

	class UnitTests {

		//Test class.
		public class TestTargetClass {

			//For the purposes of our test, we're forcing this method not to get inlined
			[MethodImpl(MethodImplOptions.NoInlining)]
			public string target(bool isUpperCase) {
				string s = GetType().Name;
				if (isUpperCase) {
					s = s.ToUpper();
				}
				return s;
			}

		}

		//Test hook
		public static string targetHook(TestTargetClass t, bool isUpperCase) {
			string s = t.GetType().Name + "HOOKED";
			if (isUpperCase) {
				s = s.ToUpper();
			}
			return s;
		}
		
		//Test static method
		public static double staticTest() {

			Random r = new Random(123);

			double average = 0.0;

			for (int k = 0; k < 100; k++) {
				average += k;
			}

			return average;

		}

		//Test hook
		public static double staticTestHook() {

			Random r = new Random(123);

			double average = 0.0;

			for (int k = 0; k < 100; k++) {
				average += k;
			}

			return average + 1.0;

		}

		//We don't support generic methods. So this is known to be unhookable. Meant to force Hook to return false.
		public static string GenericBadMethod<T>(T t) { return typeof(T).Assembly.EntryPoint.Name; }

		[UnitTest(1, "Hook/Unhook method")]
		public static void TestOne() {

			MethodInfo target = typeof(TestTargetClass).GetMethod("target");
			MethodInfo replacement = typeof(UnitTests).GetMethod("targetHook");

			HookManager hook = new HookManager();
			TestTargetClass persist = new TestTargetClass();

			//test control
			Assert.Equals(persist.target(false), "TestTargetClass");
			Assert.Equals(new TestTargetClass().target(true), "TESTTARGETCLASS");

			//test
			Assert.ExceptionSafe(() => hook.Hook(target, replacement), "hook threw exception");
			Assert.Equals(persist.target(false), "TestTargetClassHOOKED");
			Assert.Equals(new TestTargetClass().target(true), "TESTTARGETCLASSHOOKED");

			//unhook test
			Assert.ExceptionSafe(() => hook.Unhook(target), "unhook threw exception");

			//test control
			Assert.Equals(persist.target(false), "TestTargetClass");
			Assert.Equals(new TestTargetClass().target(true), "TESTTARGETCLASS");

		}

		[UnitTest(2, "Hook/Unhook static method")]
		public static void TestTwo() {

			MethodInfo target = typeof(UnitTests).GetMethod("staticTest");
			MethodInfo replacement = typeof(UnitTests).GetMethod("staticTestHook");

			HookManager hook = new HookManager();

			//test control
			Assert.Equals(staticTest(), 4950);

			//test
			Assert.ExceptionSafe(() => hook.Hook(target, replacement), "hook threw exception");
			Assert.Equals(staticTest(), 4951);

			//unhook test
			Assert.ExceptionSafe(() => hook.Unhook(target), "unhook threw exception");

			//test control
			Assert.Equals(staticTest(), 4950);

		}

		[UnitTest(3, "HookManager re-use")]
		public static void TestThree() {

			MethodInfo target = typeof(TestTargetClass).GetMethod("target");
			MethodInfo replacement = typeof(UnitTests).GetMethod("targetHook");
			MethodInfo target2 = typeof(UnitTests).GetMethod("staticTest");
			MethodInfo replacement2 = typeof(UnitTests).GetMethod("staticTestHook");

			HookManager hook = new HookManager();
			TestTargetClass persist = new TestTargetClass();

			//test control
			Assert.Equals(persist.target(false), "TestTargetClass");
			Assert.Equals(staticTest(), 4950);

			//test
			Assert.ExceptionSafe(() => hook.Hook(target, replacement), "hook1 threw exception");
			Assert.ExceptionSafe(() => hook.Hook(target2, replacement2), "hook2 threw exception");
			Assert.Equals(persist.target(true), "TESTTARGETCLASSHOOKED");
			Assert.Equals(staticTest(), 4951);

			//unhook test
			Assert.ExceptionSafe(() => hook.Unhook(target), "unhook threw exception");

			//test control
			Assert.Equals(persist.target(true), "TESTTARGETCLASS");
			Assert.Equals(staticTest(), 4951);

			//cleanup
			Assert.ExceptionSafe(() => hook.Unhook(target2), "unhook threw exception");
			Assert.Equals(staticTest(), 4950);

		}

		[UnitTest(4, "HookManager Hook exceptions")]
		public static void TestFour() {

			MethodInfo me = typeof(UnitTests).GetMethod("TestFour");
			MethodInfo bad = typeof(TestTargetClass).GetMethod("target");
			MethodInfo bad2 = typeof(UnitTests).GetMethod("GenericBadMethod");

		}

		[UnitTest(5, "HookManager Unhook exceptions")]
		public static void TestFive() {

			MethodInfo me = typeof(UnitTests).GetMethod("TestFive");

		}

		//callable from Main or an Assembly injector
		public static void DoTest(TextWriter @out = null) {

			//check if we're writing to stdout or a file
			@out = @out ?? new StreamWriter(new FileStream("Tests.log", FileMode.OpenOrCreate));

			foreach (MethodInfo currentMethod in typeof(UnitTests).GetMethods(BindingFlags.Public | BindingFlags.Static)) {

				//we do it this way because Mono 2.6 does not support currentMethod.GetCustromAttribute<T>()
				object[] attributes = currentMethod.GetCustomAttributes(false);

				//check if the current method has any attributes
				if (attributes.Length > 0) {

					for (int k = 0; k < attributes.Length; k++) {

						//check if this is a unit test
						if (attributes[k].GetType() == typeof(UnitTestAttribute)) {

							//get the current unit test
							UnitTestAttribute testAttribute = (UnitTestAttribute) attributes[k];

							try {

								@out.Write($"Test #{testAttribute.Number}: {testAttribute.Description}... ");

								//run current test
								currentMethod.Invoke(null, null);

								@out.Write($"Passed");

							} catch (TargetInvocationException e) {

								if (e.InnerException.GetType() == typeof(AssertionFailureException)) {

									@out.Write("assertion failure: " + e.InnerException.Message);

								} else {

									@out.Write("failed: ");
									@out.Write($"{e}");

								}


							} catch (Exception e) {

								@out.Write("failed: ");
								@out.Write($"{e}");

							}

							@out.WriteLine();

						}

					}

				}

			}

			//check if we're writing to a file
			if (@out != Out) {

				@out.Flush();
				@out.Close();

			}

		}

		//simple wrapper around DoTest. We still output to a file for automated testing convenience.
		public static void Main(string[] args) {
			DoTest(Out);
		}

	}

}
