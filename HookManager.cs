using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlayHooky {

	//all of this is from pinvoke.net, under their TOU
	internal class Natives {

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

		public enum PageProtection : uint {
			PAGE_NOACCESS = 0x01,
			PAGE_READONLY = 0x02,
			PAGE_READWRITE = 0x04,
			PAGE_WRITECOPY = 0x08,
			PAGE_EXECUTE = 0x10,
			PAGE_EXECUTE_READ = 0x20,
			PAGE_EXECUTE_READWRITE = 0x40,
			PAGE_EXECUTE_WRITECOPY = 0x80,
			PAGE_GUARD = 0x100,
			PAGE_NOCACHE = 0x200,
			PAGE_WRITECOMBINE = 0x400
		}

	}

	/// <summary>
	/// HookManager. Used to hook and unhook all hooked functions in a process. No method in this class is thread safe and should be shared among all threads (protected by a Lock).
	/// 
	/// Programs will most likely crash if a hook is installed while another thread is calling the target function.
	/// </summary>
	public class HookManager {

		//true if this process is x64
		private bool is64 = false;

		//Hook entries for future hook removal
		Dictionary<MethodInfo, byte[]> hooks;

		/// <summary>
		/// Creates a HookManager. HookManager is not thread safe. If it is to be used from multiple threads, one instance should be shared among all threads
		/// with a lock.
		/// </summary>
		/// <exception cref="NotImplementedException">If this architecture is not supported (IE: Not x86/x64)</exception>
		public HookManager() {

			//get processor architecture (NOTE: We do this the old way, to support .NET 2.0 (Mono 2.6, Unity 4+)
			PortableExecutableKinds kind;
			ImageFileMachine machine;
			Assembly.GetExecutingAssembly().ManifestModule.GetPEKind(out kind, out machine);

			//check if this process is x64 or x86
			if (machine == ImageFileMachine.AMD64 || machine == ImageFileMachine.I386) {

				//set architecture type for later
				is64 = (machine == ImageFileMachine.AMD64);

				//create restoration dictionary
				hooks = new Dictionary<MethodInfo, byte[]>();

			} else {

				//We only support Intel processors (for now).
				throw new NotImplementedException("Only Intel processors are supported.");

			}

		}

		/// <summary>
		/// Replaces the method call original with the method replacement with a standard x86/x64 JMP hook. The methods do not have to be in the same Assembly.
		/// Original may be static, but replacement MUST be static and accept the same arguments (if the hooked method is non-static, the first argument should be
		/// of the class type).
		/// </summary>
		/// <param name="original">MethodInfo for the function to be hooked.</param>
		/// <param name="replacement">MethodInfo for the function to replace the original function.</param>
		/// <exception cref="ArgumentNullException">If original or replacement are null.</exception>
		/// <exception cref="ArgumentException">If original and replacement are the same function, original is generic, replacement is generic or non-static, or if the target function is already hooked.</exception>
		/// <exception cref="Win32Exception">If a native call fails. This is unrecoverable.</exception>
		public unsafe void Hook(MethodInfo original, MethodInfo replacement) {

			//erroneous input check
			if ((object) original == null) throw new ArgumentNullException("original"); //original function should not be null
			if ((object) replacement == null) throw new ArgumentNullException("replacement"); //replacement function should not be null
			if ((object) original == (object) replacement) throw new ArgumentException("A function can't hook itself"); //original and replacement cannot be the same function

			//Hook sanity checks.
			if (original.IsGenericMethod) throw new ArgumentException("Original method cannot be generic"); //original function can't be generic
			if (replacement.IsGenericMethod || !replacement.IsStatic) throw new ArgumentException("Hook method must be static and non-generic"); //hook method must not be static and non-generic
			if (hooks.ContainsKey(original)) throw new ArgumentException("Attempting to hook an already hooked method"); //a method that is already hooked cannot be hooked again

			//hook function via JMP method and save the original opcodes to restore later
			byte[] originalOpcodes = PatchJMP(original, replacement);

			//List method as hooked and save the opcodes for future restoration
			hooks.Add(original, originalOpcodes);

		}

		/// <summary>
		/// Unhooks a previously hooked Method. Method must have already been hooked.
		/// </summary>
		/// <param name="original"></param>
		/// <exception cref="ArgumentNullException">If original or replacement are null.</exception>
		/// <exception cref="Win32Exception">If a native call fails. This is unrecoverable.</exception>
		public unsafe void Unhook(MethodInfo original) {

			//erroneous input checks
			if (original == null) throw new ArgumentNullException("original");

			//Hook sanity checks.
			if (!hooks.ContainsKey(original)) throw new ArgumentException("Method was never hooked"); //make sure we ever hooked the method

			//retrieve pre-hook data
			byte[] originalOpcodes = hooks[original];

			//Restore pre-jmp function
			UnhookJMP(original, originalOpcodes);

			//remove entry from restoration table
			hooks.Remove(original);

		}

		//patch the compiled JIT assembly with a primitive JMP hook
		private unsafe byte[] PatchJMP(MethodInfo original, MethodInfo replacement) {

			//JIT compile methods
			RuntimeHelpers.PrepareMethod(original.MethodHandle);
			RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

			//compile both functions and get pointers to them.
			IntPtr originalSite = original.MethodHandle.GetFunctionPointer();
			IntPtr replacementSite = replacement.MethodHandle.GetFunctionPointer();

			//instruction opcodes are 13 bytes on 64-bit, 7 bytes on 32-bit
			uint offset = (is64 ? 13u : 6u);

			//we store the original opcodes for restoration later
			byte[] originalOpcodes = new byte[offset];

			unsafe {

				//segfault protection
				uint oldProtecton = VirtualProtect(originalSite, (uint) originalOpcodes.Length, (uint) Natives.PageProtection.PAGE_EXECUTE_READWRITE);

				//get unmanaged function pointer to address of original site
				byte* originalSitePointer = (byte*) originalSite.ToPointer();

				//copy the original opcodes
				for (int k = 0; k < offset; k++) {
					originalOpcodes[k] = *(originalSitePointer + k);
				}

				//check which architecture we are patching for
				if (is64) {

					//mov r11, replacementSite
					*originalSitePointer = 0x49;
					*(originalSitePointer + 1) = 0xBB;
					*((ulong*) (originalSitePointer + 2)) = (ulong) replacementSite.ToInt64(); //sets 8 bytes

					//jmp r11
					*(originalSitePointer + 10) = 0x41;
					*(originalSitePointer + 11) = 0xFF;
					*(originalSitePointer + 12) = 0xE3;

				} else {

					//push replacementSite
					*originalSitePointer = 0x68;
					*((uint*) (originalSitePointer + 1)) = (uint) replacementSite.ToInt32(); //sets 4 bytes

					//ret
					*(originalSitePointer + 5) = 0xC3;

				}

				//flush insutruction cache to make sure our new code executes
				FlushInstructionCache(originalSite, (uint) originalOpcodes.Length);

				//done
				VirtualProtect(originalSite, (uint) originalOpcodes.Length, oldProtecton);

			}

			//return original opcodes
			return originalOpcodes;

		}

		//architecture agnostic JMP patch removal
		private unsafe void UnhookJMP(MethodInfo original, byte[] originalOpcodes) {

			IntPtr originalSite = original.MethodHandle.GetFunctionPointer();

			unsafe {

				//segfault protection
				uint oldProtecton = VirtualProtect(originalSite, (uint) originalOpcodes.Length, (uint) Natives.PageProtection.PAGE_EXECUTE_READWRITE);

				//get unmanaged function pointer to address of original site
				byte* originalSitePointer = (byte*) originalSite.ToPointer();

				//put the original bytes back where they belong
				for (int k = 0; k < originalOpcodes.Length; k++) {

					//restore current opcode to former value
					*(originalSitePointer + k) = originalOpcodes[k];

				}

				//flush insutruction cache to make sure our new code executes
				FlushInstructionCache(originalSite, (uint) originalOpcodes.Length);

				//done
				VirtualProtect(originalSite, (uint) originalOpcodes.Length, oldProtecton);

			}

		}

		//Wrapper around native method VirtualProtect. Changes the protection on a region of memory and returns the old protection level.
		private uint VirtualProtect(IntPtr address, uint size, uint protectionFlags) {

			//old protection flags for later restoration.
			uint oldProtection;

			//call VirtualProtect on the region of memory, 
			if (!Natives.VirtualProtect(address, (UIntPtr) size, protectionFlags, out oldProtection)) {

				throw new Win32Exception();

			}

			//return old protection level
			return oldProtection;

		}

		//flushes instrction cache. Required to prevent old code from being executed after we hook a function
		private void FlushInstructionCache(IntPtr address, uint size) {

			//Throw an error if FlushInstructionCache fails. This is unrecoverable.
			if (!Natives.FlushInstructionCache(Natives.GetCurrentProcess(), address, (UIntPtr) size)) {
				throw new Win32Exception();
			}

		}

	}

}
