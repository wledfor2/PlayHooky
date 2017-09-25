# PlayHooky
**PlayHooky** is a simple C# Class that can be used to hook C# Methods at runtime. **PlayHooky** works on both .NET and Mono of any version, and Unity 4-5+ as long as you are running Windows x86/x64.

This project is a generalization of the concepts presented by [sschoener](https://github.com/sschoener/cities-skylines-detour). For details on that, see below. HookManager is intended to be directly embedded in your project, not used as a class library (see caveats). As such, the only projects in this repository are for unit testing.

**PlayHooky** has been tested successfully in:
* .NET 4.5 x86/x64
* Unity 5.0 x86 (Mono 2.6)
* Unity 5.5 x64 (Mono 2.6)
* Mono 4.4 x64
* Mono 4.6 x86

It will likely work in many other environments.

# Instructions
1. Embed HookManager.cs directly in your project.
2. See the the [Example](/Example) or [Unit tests](UnitTests.cs).

If you are building TestDLL for unit testing, you will need to move mscorlib.dll and System.dll from your Unity/Mono installation into the /lib/ folder.

# Caveats
1. Hook methods must ALWAYS be static regardless of if the target method is static or not.
2. Target methods must not be generic.
3. Hooks must take the target class as the first argument, unless the hooked method is static.
4. Methods must not be inlined by the compiler before hooking.
5. Methods that are inlined by the compiler after hooking will not be restorable (no error will be generated, because we have no way to check for this).
6. HookManager.cs should ideally be embedded directly in the class library you are injecting into a target process, but should also work as a stand alone class library provided your injector handles dependencies correctly.

# Pitfalls
1. Calling the original method is not possible at this time. Doing so will overflow the stack as a hook is calling itself. See TODO.

# Technical Details (x86)
Hooks are performed by changing the first 6 bytes of a JIT'd method to the following:

```x86asm
push replacementSite
ret
```

Where replacementSite is the address of the hook function. This is a simple type of JMP using the absolute address of the target.

Our hook works for two reasons. First; in static methods, the calling convention under the hood is generally stdcall. This means that since both the target and hook method are static, the arguments are passed the same. Second; in class methods, the calling convention under the hood is a thiscall. This means the first argument passed to the method is always a pointer to the class being invoked. Therefore, our hook method takes the class "this" as an argument, which we can use to do almost anything the original method could do (we can't access private members without reflection, though). In both cases, the stack is left intact and cleaned up properly because the hook method recieves the same stack as the original method.

# Technical Details (x64)
Hooks are performed by changing the first 13 bytes of assembly of a JIT'd method to the following:

```x86asm
mov r11, replacementSite
jmp r11
```

Where replacementSite is the address of the hook function. This is a simple JMP using the absolute address of the target.

Our hook works because in x64, only fastcall is used under the hood, so we take the exact same arguments as the method we hooked. The stack is left intact and cleaned up properly because the hook method receives the same stack as the original method.

# TODO
1. Add Length Disassembler Engine for smarter hooking and trampolines.
2. Allow calling of original method using TODO #1.
3. Get rid of static methods and add a real class for simpler hooking/unhooking.

# License
MIT - See [LICENSE](LICENSE)