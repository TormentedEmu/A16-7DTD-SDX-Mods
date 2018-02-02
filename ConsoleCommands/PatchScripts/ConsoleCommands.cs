using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SDX.Compiler;
using SDX.Core;

public class ConsoleCommands : IPatcherMod
{
   public bool Patch(ModuleDefinition module)
   {
      Logging.LogInfo("Start patch process...");
      ModifyAssembly(module);
      Logging.LogInfo("Patch mod complete.");
      return true;
   }

   private void ModifyAssembly(ModuleDefinition module)
   {
      if (!ModifySdtdConsole(module))
         throw new Exception("Failed to find and modify the required method!");
   }

   private bool ModifySdtdConsole(ModuleDefinition module)
   {
      // check if the class to modify exists
      var sdtdConsole = module.Types.FirstOrDefault(c => c.Name == "SdtdConsole");
      if (sdtdConsole == null)
      {
         Logging.LogError("Failed to find class SdtdConsole.");
         return false;
      }
      // check to make sure the method still exists
      var registerCommands = sdtdConsole.Methods.FirstOrDefault(m => m.Name == "RegisterCommands");
      if (registerCommands == null)
      {
         Logging.LogError("Failed to find SdtdConsole::RegisterCommands.  Aborting patch...");
         return false;
      }

      // create the opcodes and add it to the RegisterCommands method body
      /*
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm.GetName().Name.Equals("Mods"))
				{
					loadedAssemblies.Add(asm);
					break;
				}
			}

      // What the code should closely resemble(vars will be in different locs) in IL
         9	001D	call	class [mscorlib]System.AppDomain [mscorlib]System.AppDomain::get_CurrentDomain()
         10	0022	callvirt	instance class [mscorlib]System.Reflection.Assembly[] [mscorlib]System.AppDomain::GetAssemblies()
         11	0027	stloc.3
         12	0028	ldc.i4.0
         13	0029	stloc.s	V_4 (4)
         14	002B	br.s	33 (005B) ldloc.s V_4 (4)
         15	002D	ldloc.3
         16	002E	ldloc.s	V_4 (4)
         17	0030	ldelem.ref
         18	0031	stloc.s	assembly (5)
         19	0033	ldloc.s	assembly (5)
         20	0035	callvirt	instance class [mscorlib]System.Reflection.AssemblyName [mscorlib]System.Reflection.Assembly::GetName()
         21	003A	callvirt	instance string [mscorlib]System.Reflection.AssemblyName::get_Name()
         22	003F	ldstr	"Mods"
         23	0044	callvirt	instance bool [mscorlib]System.String::Equals(string)
         24	0049	brfalse.s	29 (0055) ldloc.s V_4 (4)
         25	004B	ldloc.1
         26	004C	ldloc.s	assembly (5)
         27	004E	callvirt	instance void class [mscorlib]System.Collections.Generic.List`1<class [mscorlib]System.Reflection.Assembly>::Add(!0)
         28	0053	br.s	38 (0062) newobj instance void class [System]System.Collections.Generic.SortedList`2<string, class IConsoleCommand>::.ctor()
         29	0055	ldloc.s	V_4 (4)
         30	0057	ldc.i4.1
         31	0058	add
         32	0059	stloc.s	V_4 (4)
         33	005B	ldloc.s	V_4 (4)
         34	005D	ldloc.3
         35	005E	ldlen
         36	005F	conv.i4
         37	0060	blt.s	15 (002D) ldloc.3 
      */

      Instruction insertionStart = null;
      foreach (Instruction inst in registerCommands.Body.Instructions)
      {
         if (inst.OpCode == OpCodes.Newobj && inst.Operand.ToString() == "System.Void System.Collections.Generic.SortedList`2<System.String,IConsoleCommand>::.ctor()")
         {
            Logging.LogInfo("Found 'insertionStart' to inject opcodes.");
            insertionStart = inst;
         }
      }

      // locate vars and methods we need to create opcodes with
      VariableDefinition loadedAssemblies = registerCommands.Body.Variables.First(v => v.VariableType.ToString().Equals("System.Collections.Generic.List`1<System.Reflection.Assembly>"));
      VariableDefinition localNum = registerCommands.Body.Variables.First(v => v.VariableType.ToString().Equals("System.Int32"));
      TypeDefinition appDomain = module.GetTypeReferences().First(t => t.FullName == "System.Enum").Resolve().Module.Types.First(m => m.Name == "AppDomain");
      MethodReference getCurrentDomain = module.Import(appDomain.Methods.First(m => m.FullName == "System.AppDomain System.AppDomain::get_CurrentDomain()"));
      MethodReference getAssemblies = module.Import(appDomain.Methods.First(m => m.FullName == "System.Reflection.Assembly[] System.AppDomain::GetAssemblies()"));
      MethodReference assemblyGetName = module.Import(module.GetTypeReferences().First(t => t.FullName == "System.Reflection.Assembly").Resolve().Methods.First(m => m.FullName == "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()"));
      MethodReference assemblyNameGetName = module.Import(assemblyGetName.ReturnType.Resolve().Methods.First(m => m.FullName == "System.String System.Reflection.AssemblyName::get_Name()"));
      MethodReference stringEquals = module.Import(module.GetTypeReferences().First(t => t.FullName == "System.String").Resolve().Methods.First(m => m.FullName == "System.Boolean System.String::Equals(System.String)"));
      MethodReference listAdd = module.Import(registerCommands.Body.Variables.First(v => v.VariableType.ToString().Equals("System.Collections.Generic.List`1<System.Reflection.Assembly>")).VariableType.Resolve().Methods.First(m => m.FullName == "System.Void System.Collections.Generic.List`1::Add(T)"));
      listAdd.DeclaringType = loadedAssemblies.VariableType;

      // define and add two new local vars
      VariableDefinition localAssembly = new VariableDefinition("asm", registerCommands.Body.Variables.First(v => v.VariableType.ToString().Equals("System.Reflection.Assembly")).VariableType);
      registerCommands.Body.Variables.Add(localAssembly);

      VariableDefinition localAssemblyArray = new VariableDefinition("tmpAsmArray", getAssemblies.ReturnType);
      registerCommands.Body.Variables.Add(localAssemblyArray);

      if (insertionStart != null)
      {         
         var il = registerCommands.Body.GetILProcessor();
         Instruction branch1 = il.Create(OpCodes.Ldloc_S, localAssemblyArray);
         Instruction branch2 = il.Create(OpCodes.Ldloc_S, localNum);
         Instruction branch3 = il.Create(OpCodes.Ldloc_S, localNum);

         il.InsertBefore(insertionStart, il.Create(OpCodes.Call, getCurrentDomain));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Callvirt, getAssemblies));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Stloc_S, localAssemblyArray));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldc_I4_0));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Stloc_S, localNum));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Br_S, branch3));
         il.InsertBefore(insertionStart, branch1);
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldloc_S, localNum));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldelem_Ref));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Stloc_S, localAssembly));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldloc_S, localAssembly));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Callvirt, assemblyGetName));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Callvirt, assemblyNameGetName));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldstr, "Mods"));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Callvirt, stringEquals));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Brfalse_S, branch2));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldloc_S, loadedAssemblies));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldloc_S, localAssembly));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Callvirt, listAdd));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Br_S, insertionStart));
         il.InsertBefore(insertionStart, branch2);
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldc_I4_1));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Add));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Stloc_S, localNum));
         il.InsertBefore(insertionStart, branch3);
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldloc_S, localAssemblyArray));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Ldlen));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Conv_I4));
         il.InsertBefore(insertionStart, il.Create(OpCodes.Blt_S, branch1));
         return true;
      }

      return false;
   }

   public bool Link(ModuleDefinition gameModule, ModuleDefinition modModule)
   {
      return true;
   }

   private void SetMethodToVirtual(MethodDefinition meth)
   {
      meth.IsVirtual = true;
   }
   private void SetFieldToPublic(FieldDefinition field)
   {
      field.IsFamily = false;
      field.IsPrivate = false;
      field.IsPublic = true;
   }

   private void SetClassToPublic(TypeDefinition classDef)
   {
      if (classDef == null)
         return;

      classDef.IsPublic = true;
      classDef.IsNotPublic = false;
   }

   private void SetNestedClassToPublic(TypeDefinition classDef)
   {
      if (classDef == null)
         return;
      classDef.IsNestedPublic = true;
   }

   private void SetMethodToPublic(MethodDefinition field)
   {
      field.IsFamily = false;
      field.IsPrivate = false;
      field.IsPublic = true;
   }
}
