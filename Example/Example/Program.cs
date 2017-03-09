using System;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using PlayHooky;

namespace Example {

	//Target class
	public class TargetClass {

		//NOTE: Ideally, your target method should never become inlined. We're going to make believe here and use an Attribute.
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int add(int a, int b) {
			return a + b;
		}

		//We can't hook generics (we can only hook normal methods, non-generic methods that aren't generated at runtime). Don't even think about it!
		public T someGenericMethod<T>(T a, T b) where T : struct, IComparable<T> {
			//return max(a, b)
			return (a.CompareTo(b) > 0 ? a : b);
		}

	}

	public class Program {

		//We take the TagetClass "this" as our first argument. Don't forget this, it's important!
		//Note that this method is static. This is important.
		public static int addhook(TargetClass t, int a, int b) {

			Console.WriteLine("Hook called.");

			//Because we cannot call the original method, we must also do the work that the original did (if desired).
			return (a + b) + 1;

		}

		public static void Main(string[] args) {

			try {

				//Create the HookManager -- make sure this is done thread safe!
				HookManager manager = new HookManager();

				//Create our target class. Hooking is retroactive, so it doesn't matter if objects exist before we hook them.
				TargetClass t = new TargetClass();

				//Output the original
				Console.WriteLine("1 + 1 = " + new TargetClass().add(1, 1));

				//Hook our target method
				manager.Hook(typeof(TargetClass).GetMethod("add"), typeof(Program).GetMethod("addhook"));

				//Output the result
				Console.WriteLine("1 + 1 = " + t.add(1, 1) + "? The laws of math are breaking down!");

				//Unhook the target method
				manager.Unhook(typeof(TargetClass).GetMethod("add"));

				//Output the result... done! See?
				Console.WriteLine("1 + 1 = " + new TargetClass().add(1, 1));

			} catch(Win32Exception e) {

				//While in practice; you will never see this exception, it can happen if the underlying native calls fail. Make sure you catch it to fail gracefully!
				Console.Error.WriteLine("Unrecoverable Windows API error: " + e);

			} catch(Exception e) {

				//The only other exceptions that can be thrown are due to programmer error. For intsance, if a hook has already been hooked. Or you try to unhook a method that was never hooked.
				Console.Error.WriteLine("Unable to hook method, : " + e);

			}

		}

	}

}
