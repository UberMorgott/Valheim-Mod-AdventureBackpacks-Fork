using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Preloader;
using BepInEx.Preloader.Patching;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using UnityEngine;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using CustomAttributeArgument = Mono.Cecil.CustomAttributeArgument;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace APIManager;

internal static class Patcher
{
	private class MonoAssemblyResolver : IAssemblyResolver, IDisposable
	{
		public AssemblyDefinition Resolve(AssemblyNameReference name)
		{
			return AssemblyDefinition.ReadAssembly(AppDomain.CurrentDomain.Load(name.FullName).Location);
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			return Resolve(name);
		}

		public void Dispose()
		{
		}
	}

	private class AssemblyLoadInterceptor
	{
		private static string? assemblyPath;

		private static MethodInfo TargetMethod()
		{
			return AccessTools.DeclaredMethod(typeof(Assembly), "Load", new Type[1] { typeof(byte[]) }, (Type[])null);
		}

		private static bool Prefix(ref byte[] __0, ref Assembly? __result)
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Expected O, but got Unknown
			assemblyPath = null;
			if (modifyNextLoad)
			{
				modifyNextLoad = false;
				try
				{
					AssemblyDefinition val = AssemblyDefinition.ReadAssembly((Stream)new MemoryStream(__0), new ReaderParameters
					{
						AssemblyResolver = (IAssemblyResolver)(object)new MonoAssemblyResolver()
					});
					try
					{
						((Dictionary<string, string>)typeof(EnvVars).Assembly.GetType("BepInEx.Preloader.RuntimeFixes.UnityPatches").GetProperty("AssemblyLocations").GetValue(null))[val.FullName] = currentAssemblyPath;
						FixupModuleReferences(val.MainModule);
						using MemoryStream memoryStream = new MemoryStream();
						val.Write((Stream)memoryStream);
						__0 = memoryStream.ToArray();
						string path = dumpedAssembliesPath + Path.DirectorySeparatorChar + ((AssemblyNameReference)val.Name).Name + ".dll";
						if (loadDumpedAssemblies.Value || dumpAssemblies.Value)
						{
							File.WriteAllBytes(path, __0);
						}
						if (loadDumpedAssemblies.Value)
						{
							assemblyPath = path;
							__result = null;
							return false;
						}
					}
					finally
					{
						((IDisposable)val)?.Dispose();
					}
				}
				catch (BadImageFormatException)
				{
				}
				catch (Exception ex2)
				{
					Debug.LogError((object)("Failed patching ... " + ex2));
				}
			}
			return true;
		}

		private static void Postfix(ref Assembly? __result)
		{
			if (assemblyPath != null && __result == null)
			{
				__result = Assembly.LoadFrom(assemblyPath);
			}
		}
	}

	private static PluginInfo? lastPluginInfo;

	private static bool modifyNextLoad = false;

	private static string modGUID = null;

	private static HashSet<string> redirectedNamespaces = null;

	private static readonly Assembly patchingAssembly = Assembly.GetExecutingAssembly();

	private static string currentAssemblyPath = null;

	private static readonly ConfigEntry<bool> dumpAssemblies = (ConfigEntry<bool>)AccessTools.DeclaredField(typeof(AssemblyPatcher), "ConfigDumpAssemblies").GetValue(null);

	private static readonly ConfigEntry<bool> loadDumpedAssemblies = (ConfigEntry<bool>)AccessTools.DeclaredField(typeof(AssemblyPatcher), "ConfigLoadDumpedAssemblies").GetValue(null);

	private static readonly string dumpedAssembliesPath = (string)AccessTools.DeclaredField(typeof(AssemblyPatcher), "DumpedAssembliesPath").GetValue(null);

	private static void GrabPluginInfo(PluginInfo __instance)
	{
		lastPluginInfo = __instance;
	}

	[HarmonyPriority(700)]
	private static void CheckAssemblyLoadFile(string __0)
	{
		PluginInfo? obj = lastPluginInfo;
		if (__0 == ((obj != null) ? obj.Location : null) && lastPluginInfo.Dependencies.Any((BepInDependency d) => d.DependencyGUID == modGUID))
		{
			modifyNextLoad = true;
			currentAssemblyPath = __0;
		}
		lastPluginInfo = null;
	}

	[HarmonyPriority(500)]
	private static bool InterceptAssemblyLoadFile(string __0, ref Assembly? __result)
	{
		if (modifyNextLoad && (object)__result == null)
		{
			__result = Assembly.Load(File.ReadAllBytes(__0));
			return false;
		}
		return true;
	}

	private static void FixupModuleReferences(ModuleDefinition module)
	{
		foreach (TypeDefinition type3 in module.GetTypes())
		{
			if ((object)patchingAssembly.GetType(((MemberReference)type3).FullName) == null)
			{
				Dispatch(type3);
			}
		}
		static bool AreSame(TypeReference a, TypeReference b)
		{
			return (bool)typeof(MetadataResolver).Assembly.GetType("Mono.Cecil.MetadataResolver").GetMethod("AreSame", BindingFlags.Static | BindingFlags.NonPublic, null, new Type[2]
			{
				typeof(TypeReference),
				typeof(TypeReference)
			}, null).Invoke(null, new object[2] { a, b });
		}
		void Dispatch(TypeDefinition type)
		{
			if (type.BaseType != null && (object)type.BaseType.Scope == module && redirectedNamespaces.Contains(baseDeclaringType(type.BaseType).Namespace))
			{
				Type type2 = patchingAssembly.GetType(((MemberReference)type.BaseType).FullName);
				if ((object)type2 != null)
				{
					type.BaseType = module.ImportReference(type2);
				}
			}
			DispatchGenericParameters((IGenericParameterProvider)(object)type, ((MemberReference)type).FullName);
			DispatchInterfaces(type, ((MemberReference)type).FullName);
			DispatchAttributes((ICustomAttributeProvider)(object)type, ((MemberReference)type).FullName);
			DispatchFields(type, ((MemberReference)type).FullName);
			DispatchProperties(type, ((MemberReference)type).FullName);
			DispatchEvents(type, ((MemberReference)type).FullName);
			DispatchMethods(type);
		}
		void DispatchAttributes(ICustomAttributeProvider provider, string referencingEntityName)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			//IL_009f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_010d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0112: Unknown result type (might be due to invalid IL or missing references)
			//IL_0126: Unknown result type (might be due to invalid IL or missing references)
			//IL_012b: Unknown result type (might be due to invalid IL or missing references)
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0141: Unknown result type (might be due to invalid IL or missing references)
			//IL_014a: Unknown result type (might be due to invalid IL or missing references)
			//IL_014f: Unknown result type (might be due to invalid IL or missing references)
			if (!provider.HasCustomAttributes)
			{
				return;
			}
			var enumerator2 = provider.CustomAttributes.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					CustomAttribute current2 = enumerator2.Current;
					MethodReference val = importMethodReference(current2.Constructor);
					if (val != null)
					{
						current2.Constructor = val;
					}
					else
					{
						VisitMethod(current2.Constructor, referencingEntityName);
					}
					for (int i = 0; i < current2.ConstructorArguments.Count; i++)
					{
						CustomAttributeArgument val2 = current2.ConstructorArguments[i];
						current2.ConstructorArguments[i] = new CustomAttributeArgument(VisitType(val2.Type, referencingEntityName), val2.Value);
					}
					CustomAttributeArgument argument;
					for (int j = 0; j < current2.Properties.Count; j++)
					{
						CustomAttributeNamedArgument val3 = current2.Properties[j];
						Collection<CustomAttributeNamedArgument> properties = current2.Properties;
						int num = j;
						string name = val3.Name;
						argument = val3.Argument;
						TypeReference? obj = VisitType(argument.Type, referencingEntityName);
						argument = val3.Argument;
						properties[num] = new CustomAttributeNamedArgument(name, new CustomAttributeArgument(obj, argument.Value));
					}
					for (int k = 0; k < current2.Fields.Count; k++)
					{
						CustomAttributeNamedArgument val4 = current2.Fields[k];
						Collection<CustomAttributeNamedArgument> fields = current2.Fields;
						int num2 = k;
						string name2 = val4.Name;
						argument = val4.Argument;
						TypeReference? obj2 = VisitType(argument.Type, referencingEntityName);
						argument = val4.Argument;
						fields[num2] = new CustomAttributeNamedArgument(name2, new CustomAttributeArgument(obj2, argument.Value));
					}
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchEvents(TypeDefinition type, string referencingEntityName)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = type.Events.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					EventDefinition current2 = enumerator2.Current;
					((EventReference)current2).EventType = VisitType(((EventReference)current2).EventType, referencingEntityName);
					DispatchAttributes((ICustomAttributeProvider)(object)current2, referencingEntityName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchFields(TypeDefinition type, string referencingEntityName)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = type.Fields.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					FieldDefinition current2 = enumerator2.Current;
					((FieldReference)current2).FieldType = VisitType(((FieldReference)current2).FieldType, referencingEntityName);
					DispatchAttributes((ICustomAttributeProvider)(object)current2, referencingEntityName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchGenericArguments(IGenericInstance genericInstance, string referencingEntityName)
		{
			for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
			{
				genericInstance.GenericArguments[i] = VisitType(genericInstance.GenericArguments[i], referencingEntityName);
			}
		}
		void DispatchGenericParameters(IGenericParameterProvider provider, string referencingEntityName)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = provider.GenericParameters.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					GenericParameter current2 = enumerator2.Current;
					DispatchAttributes((ICustomAttributeProvider)(object)current2, referencingEntityName);
					for (int i = 0; i < current2.Constraints.Count; i++)
					{
						current2.Constraints[i] = VisitType(current2.Constraints[i], referencingEntityName);
					}
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchInterfaces(TypeDefinition type, string referencingEntityName)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = type.Interfaces.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					InterfaceImplementation current2 = enumerator2.Current;
					current2.InterfaceType = VisitType(current2.InterfaceType, referencingEntityName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchMethod(MethodDefinition method)
		{
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			((MethodReference)method).ReturnType = VisitType(((MethodReference)method).ReturnType, ((MemberReference)method).FullName);
			DispatchAttributes((ICustomAttributeProvider)(object)((MethodReference)method).MethodReturnType, ((MemberReference)method).FullName);
			DispatchGenericParameters((IGenericParameterProvider)(object)method, ((MemberReference)method).FullName);
			var enumerator2 = ((MethodReference)method).Parameters.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					ParameterDefinition current2 = enumerator2.Current;
					((ParameterReference)current2).ParameterType = VisitType(((ParameterReference)current2).ParameterType, ((MemberReference)method).FullName);
					DispatchAttributes((ICustomAttributeProvider)(object)current2, ((MemberReference)method).FullName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
			for (int i = 0; i < method.Overrides.Count; i++)
			{
				MethodReference val = importMethodReference(method.Overrides[i]);
				if (val != null)
				{
					method.Overrides[i] = val;
				}
				else
				{
					VisitMethod(method.Overrides[i], ((MemberReference)method).FullName);
				}
			}
			if (method.HasBody)
			{
				DispatchMethodBody(method.Body);
			}
		}
		void DispatchMethodBody(MethodBody body)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = body.Variables.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					VariableDefinition current2 = enumerator2.Current;
					((VariableReference)current2).VariableType = VisitType(((VariableReference)current2).VariableType, ((MemberReference)body.Method).FullName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
			var enumerator3 = body.Instructions.GetEnumerator();
			try
			{
				while (enumerator3.MoveNext())
				{
					Instruction current3 = enumerator3.Current;
					object operand = current3.Operand;
					FieldReference val = (FieldReference)((operand is FieldReference) ? operand : null);
					if (val == null)
					{
						MethodReference val2 = (MethodReference)((operand is MethodReference) ? operand : null);
						if (val2 == null)
						{
							TypeReference val3 = (TypeReference)((operand is TypeReference) ? operand : null);
							if (val3 != null)
							{
								current3.Operand = VisitType(val3, ((MemberReference)body.Method).FullName);
							}
						}
						else
						{
							MethodReference val4 = importMethodReference(val2);
							if (val4 != null)
							{
								current3.Operand = val4;
							}
							else
							{
								VisitMethod(val2, ((MemberReference)body.Method).FullName);
							}
						}
					}
					else
					{
						FieldReference val5 = importFieldReference(val);
						if (val5 != null)
						{
							current3.Operand = val5;
						}
						else
						{
							VisitField(val, ((MemberReference)body.Method).FullName);
						}
					}
				}
			}
			finally
			{
				((IDisposable)enumerator3/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchMethods(TypeDefinition type)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = type.Methods.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					MethodDefinition current2 = enumerator2.Current;
					DispatchMethod(current2);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		void DispatchProperties(TypeDefinition type, string referencingEntityName)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			var enumerator2 = type.Properties.GetEnumerator();
			try
			{
				while (enumerator2.MoveNext())
				{
					PropertyDefinition current2 = enumerator2.Current;
					((PropertyReference)current2).PropertyType = VisitType(((PropertyReference)current2).PropertyType, referencingEntityName);
					DispatchAttributes((ICustomAttributeProvider)(object)current2, referencingEntityName);
				}
			}
			finally
			{
				((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
			}
		}
		TypeReference FixupType(TypeReference type)
		{
			if ((object)type.Scope == module && redirectedNamespaces.Contains(baseDeclaringType(type).Namespace))
			{
				if (type.IsNested)
				{
					return FixupType(((MemberReference)type).DeclaringType);
				}
				Type type2 = patchingAssembly.GetType(((MemberReference)type).FullName);
				if ((object)type2 != null)
				{
					return module.ImportReference(type2);
				}
			}
			return type;
		}
		void VisitField(FieldReference? field, string referencingEntityName)
		{
			if (field != null)
			{
				field.FieldType = VisitType(field.FieldType, referencingEntityName);
				if (!(field is FieldDefinition))
				{
					((MemberReference)field).DeclaringType = VisitType(((MemberReference)field).DeclaringType, referencingEntityName);
				}
			}
		}
		void VisitMethod(MethodReference? method, string referencingEntityName)
		{
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Unknown result type (might be due to invalid IL or missing references)
			if (method != null)
			{
				GenericInstanceMethod val = (GenericInstanceMethod)(object)((method is GenericInstanceMethod) ? method : null);
				if (val != null)
				{
					DispatchGenericArguments((IGenericInstance)(object)val, referencingEntityName);
				}
				method.ReturnType = VisitType(method.ReturnType, referencingEntityName);
				var enumerator2 = method.Parameters.GetEnumerator();
				try
				{
					while (enumerator2.MoveNext())
					{
						ParameterDefinition current2 = enumerator2.Current;
						((ParameterReference)current2).ParameterType = VisitType(((ParameterReference)current2).ParameterType, referencingEntityName);
					}
				}
				finally
				{
					((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
				}
				if (!(method is MethodSpecification))
				{
					((MemberReference)method).DeclaringType = VisitType(((MemberReference)method).DeclaringType, referencingEntityName);
				}
			}
		}
		TypeReference? VisitType(TypeReference? type, string referencingEntityName)
		{
			if (type == null)
			{
				return type;
			}
			if (type.GetElementType().IsGenericParameter)
			{
				return type;
			}
			GenericInstanceType val = (GenericInstanceType)(object)((type is GenericInstanceType) ? type : null);
			if (val != null)
			{
				DispatchGenericArguments((IGenericInstance)(object)val, referencingEntityName);
			}
			return FixupType(type);
		}
		static TypeReference baseDeclaringType(TypeReference type)
		{
			while (((MemberReference)type).DeclaringType != null)
			{
				type = ((MemberReference)type).DeclaringType;
			}
			return type;
		}
		FieldReference? importFieldReference(FieldReference field)
		{
			if ((object)((MemberReference)field).DeclaringType.Scope == module && redirectedNamespaces.Contains(baseDeclaringType(((MemberReference)field).DeclaringType).Namespace))
			{
				FieldInfo fieldInfo = patchingAssembly.GetType(((MemberReference)((MemberReference)field).DeclaringType).FullName)?.GetField(((MemberReference)field).Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if ((object)fieldInfo != null)
				{
					return module.ImportReference(fieldInfo);
				}
			}
			return null;
		}
		MethodReference? importMethodReference(MethodReference method)
		{
			//IL_0176: Unknown result type (might be due to invalid IL or missing references)
			//IL_017d: Expected O, but got Unknown
			if ((object)((MemberReference)method).DeclaringType.Scope == module && redirectedNamespaces.Contains(baseDeclaringType(((MemberReference)method).DeclaringType).Namespace))
			{
				if (((MemberReference)method).Name == ".cctor")
				{
					ConstructorInfo[] array = patchingAssembly.GetType(((MemberReference)((MemberReference)method).DeclaringType).FullName)?.GetConstructors(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					if (array != null && array.Length == 1)
					{
						return module.ImportReference((MethodBase)array[0]);
					}
				}
				else if (((MemberReference)method).Name == ".ctor")
				{
					ConstructorInfo constructorInfo = patchingAssembly.GetType(((MemberReference)((MemberReference)method).DeclaringType).FullName)?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(CompareMethods);
					if ((object)constructorInfo != null)
					{
						return module.ImportReference((MethodBase)constructorInfo);
					}
				}
				else
				{
					MethodInfo methodInfo = patchingAssembly.GetType(((MemberReference)((MemberReference)method).DeclaringType).FullName)?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
					{
						if (m.Name != ((MemberReference)method).Name)
						{
							return false;
						}
						return (((MemberReference)method.ReturnType).ContainsGenericParameter == m.ReturnType.IsGenericParameter || AreSame(method.ReturnType, module.ImportReference(m.ReturnType))) && CompareMethods(m);
					});
					if ((object)methodInfo != null)
					{
						MethodReference val = module.ImportReference((MethodBase)methodInfo);
						MethodReference obj = method;
						GenericInstanceMethod val2 = (GenericInstanceMethod)(object)((obj is GenericInstanceMethod) ? obj : null);
						if (val2 != null)
						{
							GenericInstanceMethod val3 = new GenericInstanceMethod(val);
							for (int num = 0; num < val2.GenericArguments.Count; num++)
							{
								val2.GenericArguments[num] = VisitType(val2.GenericArguments[num], ((MemberReference)method).FullName);
								val3.GenericArguments.Add(val2.GenericArguments[num]);
							}
							val = (MethodReference)(object)val3;
						}
						return val;
					}
				}
			}
			return null;
			bool CompareMethods(MethodBase m)
			{
				ParameterInfo[] parameters = m.GetParameters();
				if (method.IsGenericInstance != m.IsGenericMethodDefinition || parameters.Length != 0 != method.HasParameters)
				{
					return false;
				}
				if (method.HasParameters)
				{
					if (method.Parameters.Count != parameters.Length)
					{
						return false;
					}
					for (int i = 0; i < method.Parameters.Count; i++)
					{
						if (parameters[i].ParameterType.IsGenericParameter ? (!((ParameterReference)method.Parameters[i]).ParameterType.IsGenericParameter) : (!AreSame(((ParameterReference)method.Parameters[i]).ParameterType, module.ImportReference(parameters[i].ParameterType))))
						{
							return false;
						}
					}
				}
				return true;
			}
		}
	}

	public static void Patch(IEnumerable<string>? extraNamespaces = null)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Expected O, but got Unknown
		Harmony val = new Harmony("org.bepinex.plugins.APIManager");
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(PluginInfo), "ToString", (Type[])null, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Patcher), "GrabPluginInfo", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Assembly), "LoadFile", new Type[1] { typeof(string) }, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Patcher), "InterceptAssemblyLoadFile", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Assembly), "LoadFile", new Type[1] { typeof(string) }, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Patcher), "CheckAssemblyLoadFile", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		new PatchClassProcessor(val, typeof(AssemblyLoadInterceptor), true).Patch();
		IEnumerable<TypeInfo> source;
		try
		{
			source = patchingAssembly.DefinedTypes.ToList();
		}
		catch (ReflectionTypeLoadException ex)
		{
			source = from t in ex.Types
				where t != null
				select t.GetTypeInfo();
		}
		BaseUnityPlugin val2 = (BaseUnityPlugin)Chainloader.ManagerObject.GetComponent((Type)source.First((TypeInfo t) => t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));
		redirectedNamespaces = new HashSet<string>(extraNamespaces ?? Array.Empty<string>()) { ((object)val2).GetType().Namespace };
		modGUID = val2.Info.Metadata.GUID;
	}
}
