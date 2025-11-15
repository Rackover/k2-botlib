// Disable warnings about XML documentation
#pragma warning disable 1591

using System;
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing math Lua functions 
	/// </summary>
	[MoonSharpModule(Namespace = "math")]
	public class MathModule
	{
		[MoonSharpModuleConstant]
        public static readonly FixedNumber pi = FixedNumber.PI;
		[MoonSharpModuleConstant]
		public static readonly FixedNumber huge = FixedNumber.MaxValue;

		public static void MoonSharpInit(Table globalTable, Table ioTable)
		{

		}

		private static DynValue exec1(CallbackArguments args, string funcName, Func<FixedNumber, FixedNumber> func)
		{
			DynValue arg = args.AsType(0, funcName, DataType.Number, false);
			return DynValue.NewNumber(func(arg.Number));
		}

		private static DynValue exec2(CallbackArguments args, string funcName, Func<FixedNumber, FixedNumber, FixedNumber> func)
		{
			DynValue arg = args.AsType(0, funcName, DataType.Number, false);
			DynValue arg2 = args.AsType(1, funcName, DataType.Number, false);
			return DynValue.NewNumber(func(arg.Number, arg2.Number));
		}
		private static DynValue exec2n(CallbackArguments args, string funcName, FixedNumber defVal, Func<FixedNumber, FixedNumber, FixedNumber> func)
		{
			DynValue arg = args.AsType(0, funcName, DataType.Number, false);
			DynValue arg2 = args.AsType(1, funcName, DataType.Number, true);

			return DynValue.NewNumber(func(arg.Number, arg2.IsNil() ? defVal : arg2.Number));
		}
		private static DynValue execaccum(CallbackArguments args, string funcName, Func<FixedNumber, FixedNumber, FixedNumber> func)
		{
            FixedNumber accum = default;

			if (args.Count == 0)
			{
				throw new ScriptRuntimeException("bad argument #1 to '{0}' (number expected, got no value)", funcName);
			}

			for (int i = 0; i < args.Count; i++)
			{
				DynValue arg = args.AsType(i, funcName, DataType.Number, false);

				if (i == 0)
					accum = arg.Number;
				else
					accum = func(accum, arg.Number);
			}

			return DynValue.NewNumber(accum);
		}


		[MoonSharpModuleMethod]
		public static DynValue abs(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "abs", d => Math.Abs(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue acos(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "acos", d => Math.Acos(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue asin(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "asin", d => Math.Asin(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue atan(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "atan", d => Math.Atan(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue atan2(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec2(args, "atan2", (d1, d2) => Math.Atan2(d1, d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue ceil(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "ceil", d => FixedNumber.Ceil(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue cos(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "cos", d => Math.Cos(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue cosh(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "cosh", d => Math.Cosh(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue deg(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "deg", d => (d * 180) / FixedNumber.PI);
		}

		[MoonSharpModuleMethod]
		public static DynValue exp(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "exp", d => Math.Exp(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue floor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "floor", d => FixedNumber.Floor(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue fmod(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec2(args, "fmod", (d1, d2) => FixedNumber.IEEERemainder(d1, d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue ldexp(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec2(args, "ldexp", (d1, d2) => d1 * FixedNumber.Pow(2, d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue log(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec2n(args, "log", FixedNumber.E, (d1, d2) => FixedNumber.Log(d1, d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue max(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return execaccum(args, "max", (d1, d2) => Math.Max((long)d1, (long)d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue min(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return execaccum(args, "min", (d1, d2) => Math.Min((long)d1, (long)d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue modf(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue arg = args.AsType(0, "modf", DataType.Number, false);
			return DynValue.NewTuple(DynValue.NewNumber(FixedNumber.Floor(arg.Number)), DynValue.NewNumber(arg.Number - FixedNumber.Floor(arg.Number)));
		}


		[MoonSharpModuleMethod]
		public static DynValue pow(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec2(args, "pow", (d1, d2) => FixedNumber.Pow(d1, d2));
		}

		[MoonSharpModuleMethod]
		public static DynValue rad(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "rad", d => (d * FixedNumber.PI) / 180);
		}

		[MoonSharpModuleMethod]
		public static DynValue sin(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "sin", d => Math.Sin(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue sinh(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "sinh", d => Math.Sinh(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue sqrt(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "sqrt", d => Math.Sqrt(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue tan(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "tan", d => Math.Tan(d));
		}

		[MoonSharpModuleMethod]
		public static DynValue tanh(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return exec1(args, "tanh", d => Math.Tanh(d));
		}


	}
}
