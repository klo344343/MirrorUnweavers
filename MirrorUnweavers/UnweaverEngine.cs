using System;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System.Collections.Generic;

namespace MirrorUnweavers
{
    public class UnweaverEngine
    {
        private readonly ModuleDefMD _module;
        private readonly HashSet<IMethod> _methodsToRemove = [];
        private readonly HashSet<IDnlibDef> _keptMembers = [];

        public UnweaverEngine(ModuleDefMD module) => _module = module;

        public void Run()
        {
            var allTypes = _module.GetTypes().Where(t => t.IsClass).ToList();

            foreach (var type in allTypes)
            {
                RestoreUserCode(type);
                MarkMirrorGarbage(type);
            }

            CleanInstructionsDeeply();
            FinalizeRemoval();
        }

        private void RestoreUserCode(TypeDef type)
        {
            var methods = type.Methods.ToList();
            foreach (var userMethod in methods.Where(m => m.Name.String.StartsWith("UserCode_")))
            {
                string cleanName = userMethod.Name.String.Replace("UserCode_", "").Split(["__"], StringSplitOptions.None)[0];
                var original = type.Methods.FirstOrDefault(m => m.Name.String == cleanName);

                if (original != null && userMethod.HasBody)
                {
                    original.Body = userMethod.Body;
                    _methodsToRemove.Add(userMethod);
                }
            }
        }

        private void MarkMirrorGarbage(TypeDef type)
        {
            string[] mirrorMagicNames = [
            "SerializeSyncVars", "DeserializeSyncVars", "OnSerialize", "OnDeserialize",
            "Weaved", "InvokeUserCode", "Mirror.RemoteCalls"
        ];

            foreach (var m in type.Methods)
            {
                if (mirrorMagicNames.Any(name => m.Name.String.Contains(name)))
                    _methodsToRemove.Add(m);
            }
        }

        private void CleanInstructionsDeeply()
        {
            foreach (var type in _module.GetTypes().Where(t => t.HasMethods))
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    if (method.Name == ".cctor" && HasMirrorCalls(method))
                    {
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
                        continue;
                    }

                    var instructions = method.Body.Instructions;
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        var instr = instructions[i];
                        if (instr.OpCode.FlowControl != FlowControl.Call) continue;

                        if (instr.Operand is not IMethod target) continue;

                        string methodName = target.Name.String;
                        bool isSet = methodName.StartsWith("set_Network");
                        bool isGet = methodName.StartsWith("get_Network");

                        if (isSet || isGet)
                        {
                            string pureName = methodName.Substring(11);
                            ITypeDefOrRef searchStartType = target.DeclaringType;

                            if (TryRestoreMember(instr, searchStartType, pureName, isSet))
                            {
                                continue;
                            }
                        }

                        if (IsMirrorTrash(target) || isSet || isGet)
                        {
                            RemoveInstructionAndCleanupStack(method, i);
                            i = Math.Max(0, i - 1);
                        }
                    }
                    method.Body.OptimizeMacros();
                    method.Body.OptimizeBranches();
                }
            }
        }

        private bool TryRestoreMember(Instruction instr, ITypeDefOrRef typeRef, string name, bool isSet)
        {
            if (typeRef == null) return false;
            TypeDef typeDef = typeRef.ResolveTypeDef();

            while (typeDef != null)
            {
                var prop = typeDef.Properties.FirstOrDefault(p => p.Name.String == name);
                if (prop != null)
                {
                    MethodDef propMethod = isSet ? prop.SetMethod : prop.GetMethod;
                    if (propMethod != null)
                    {
                        instr.OpCode = OpCodes.Callvirt;
                        instr.Operand = propMethod;
                        _keptMembers.Add(propMethod);
                        return true;
                    }
                }

                var field = typeDef.Fields.FirstOrDefault(f => f.Name.String == name || f.Name.String == "___" + name);
                if (field != null)
                {
                    instr.OpCode = isSet ? OpCodes.Stfld : OpCodes.Ldfld;
                    instr.Operand = field;
                    _keptMembers.Add(field);
                    return true;
                }

                if (typeDef.BaseType == null) break;
                typeDef = typeDef.BaseType.ResolveTypeDef();
            }
            return false;
        }

        private void RemoveInstructionAndCleanupStack(MethodDef method, int index)
        {
            var instructions = method.Body.Instructions;
            var instr = instructions[index];
            var target = (IMethod)instr.Operand;

            int popCount = 0;
            bool hasReturnValue = false;

            if (target.MethodSig != null)
            {
                popCount = target.MethodSig.Params.Count;
                if (target.MethodSig.HasThis) popCount++;
                hasReturnValue = target.MethodSig.RetType.ElementType != ElementType.Void;
            }

            instr.OpCode = OpCodes.Nop;
            instr.Operand = null;

            for (int j = 0; j < popCount; j++)
            {
                instructions.Insert(index + 1, OpCodes.Pop.ToInstruction());
            }

            if (hasReturnValue)
            {
                instructions.Insert(index + 1 + popCount, OpCodes.Ldnull.ToInstruction());
            }
        }

        private bool HasMirrorCalls(MethodDef method)
        {
            return method.Body.Instructions.Any(i => i.Operand is IMethod target && IsMirrorTrash(target));
        }

        private bool IsMirrorTrash(IMethod target)
        {
            if (target == null) return false;
            string name = target.Name.String;
            string full = target.FullName;
            string declType = target.DeclaringType?.FullName ?? "";

            return name.Contains("Serialize") ||
                   name.Contains("Deserialize") ||
                   full.Contains("Mirror.RemoteCalls") ||
                   declType.Contains("Mirror") ||
                   full.Contains("GeneratedSyncVar") ||
                   full.Contains("SendCommandInternal") ||
                   full.Contains("SendRPCInternal") ||
                   full.Contains("NetworkBehaviour::get_syncVar") ||
                   full.Contains("NetworkBehaviour::set_syncVar") ||
                   name == "InvokeUserCode" ||
                   _methodsToRemove.Any(m => m.FullName == full);
        }

        private void FinalizeRemoval()
        {
            foreach (var type in _module.GetTypes())
            {
                var methodsToRemove = type.Methods.Where(m =>
                    (m.Name.String.StartsWith("get_Network") ||
                     m.Name.String.StartsWith("set_Network") ||
                     m.Name.String.Contains("GeneratedSyncVar") ||
                     _methodsToRemove.Contains(m))
                    && !_keptMembers.Contains(m)
                ).ToList();

                foreach (var m in methodsToRemove) type.Methods.Remove(m);

                var propsToRem = type.Properties.Where(p =>
                    p.Name.String.StartsWith("Network") ||
                    ((p.GetMethod != null && _methodsToRemove.Contains(p.GetMethod)) ||
                     (p.SetMethod != null && _methodsToRemove.Contains(p.SetMethod)))
                ).ToList();

                foreach (var p in propsToRem) type.Properties.Remove(p);

                var fieldsToRem = type.Fields.Where(f =>
                    (f.Name.String.StartsWith("___") || f.Name.String.Contains("MirrorSyncVar"))
                    && !_keptMembers.Contains(f)
                ).ToList();

                foreach (var f in fieldsToRem) type.Fields.Remove(f);
            }

            var genClass = _module.Types.FirstOrDefault(t => t.Name.String == "GeneratedNetworkCode");
            if (genClass != null) _module.Types.Remove(genClass);
        }
    }
}
