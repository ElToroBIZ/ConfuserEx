﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Confuser.Core;
using Confuser.Core.Services;
using Confuser.Renamer.Services;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Confuser.Renamer.Analyzers {
	public class WinFormsAnalyzer : IRenamer {
		Dictionary<string, List<PropertyDef>> properties = new Dictionary<string, List<PropertyDef>>();

		public void Analyze(IConfuserContext context, INameService service, IProtectionParameters parameters,
			IDnlibDef def) {
			if (def is ModuleDef) {
				foreach (var type in ((ModuleDef)def).GetTypes())
				foreach (var prop in type.Properties)
					properties.AddListEntry(prop.Name, prop);
				return;
			}

			if (!(def is MethodDef method) || !method.HasBody)
				return;

			AnalyzeMethod(context, service, method);
		}

		void AnalyzeMethod(IConfuserContext context, INameService service, MethodDef method) {
			var binding = new List<Tuple<bool, Instruction>>();
			var dataPropertyName = new List<Instruction>();
			foreach (var instr in method.Body.Instructions) {
				var target = instr.Operand as IMethod;
				switch (instr.OpCode.Code) {
					case Code.Call:
					case Code.Callvirt:
						Debug.Assert(target != null);

						if ((target.DeclaringType.FullName == "System.Windows.Forms.ControlBindingsCollection" ||
						     target.DeclaringType.FullName == "System.Windows.Forms.BindingsCollection") &&
						    target.Name == "Add" && target.MethodSig.Params.Count != 1) {
							binding.Add(Tuple.Create(true, instr));
						}
						else if (target.DeclaringType.FullName == "System.Windows.Forms.DataGridViewColumn" &&
						         target.Name == "set_DataPropertyName" &&
						         target.MethodSig.Params.Count == 1) {
							dataPropertyName.Add(instr);
						}

						break;
					case Code.Newobj:
						Debug.Assert(target != null);
						if (target.DeclaringType.FullName == "System.Windows.Forms.Binding" &&
						    target.Name.String == ".ctor") {
							binding.Add(Tuple.Create(false, instr));
						}

						break;
				}
			}

			if (binding.Count == 0 && dataPropertyName.Count == 0)
				return;

			var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger(NameProtection._Id);

			var traceSrv = context.Registry.GetRequiredService<ITraceService>();
			var trace = traceSrv.Trace(method);

			bool erred = false;
			foreach (var instrInfo in binding) {
				int[] args = trace.TraceArguments(instrInfo.Item2);
				if (args == null) {
					if (!erred)
						logger.LogWarning("Failed to extract binding property name in '{0}'.", method.FullName);
					erred = true;
					continue;
				}

				var argumentIndex = (instrInfo.Item1 ? 1 : 0);
				var propertyName = ResolveNameInstruction(method, args, ref argumentIndex);
				if (propertyName.OpCode.Code != Code.Ldstr) {
					if (!erred)
						logger.LogWarning("Failed to extract binding property name in '{0}'.", method.FullName);
					erred = true;
				}
				else {
					List<PropertyDef> props;
					if (!properties.TryGetValue((string)propertyName.Operand, out props)) {
						if (!erred)
							logger.LogWarning("Failed to extract target property in '{0}'.", method.FullName);
						erred = true;
					}
					else {
						foreach (var property in props)
							service.SetCanRename(context, property, false);
					}
				}

				argumentIndex += 2;
				var dataMember = ResolveNameInstruction(method, args, ref argumentIndex);
				if (dataMember.OpCode.Code != Code.Ldstr) {
					if (!erred)
						logger.LogWarning("Failed to extract binding property name in '{0}'.", method.FullName);
					erred = true;
				}
				else {
					List<PropertyDef> props;
					if (!properties.TryGetValue((string)dataMember.Operand, out props)) {
						if (!erred)
							logger.LogWarning("Failed to extract target property in '{0}'.", method.FullName);
						erred = true;
					}
					else {
						foreach (var property in props)
							service.SetCanRename(context, property, false);
					}
				}
			}

			foreach (var instrInfo in dataPropertyName) {
				int[] args = trace.TraceArguments(instrInfo);
				if (args == null) {
					if (!erred)
						logger.LogWarning("Failed to extract binding property name in '{0}'.", method.FullName);
					erred = true;
					continue;
				}

				var argumentIndex = 0;
				var propertyName = ResolveNameInstruction(method, args, ref argumentIndex);
				if (propertyName.OpCode.Code != Code.Ldstr) {
					if (!erred)
						logger.LogWarning("Failed to extract binding property name in '{0}'.", method.FullName);
					erred = true;
				}
				else {
					if (!properties.TryGetValue((string)propertyName.Operand, out var props)) {
						if (!erred)
							logger.LogWarning("Failed to extract target property in '{0}'.", method.FullName);
						erred = true;
					}
					else {
						foreach (var property in props)
							service.SetCanRename(context, property, false);
					}
				}
			}
		}

		private static Instruction ResolveNameInstruction(MethodDef method, int[] tracedArguments,
			ref int argumentIndex) {
			Instruction propertyName = null;
			for (;;) {
				propertyName = method.Body.Instructions[tracedArguments[argumentIndex]];
				if (propertyName.OpCode.Code == Code.Dup)
					argumentIndex++;
				else break;
			}

			return propertyName;
		}


		public void PreRename(IConfuserContext context, INameService service, IProtectionParameters parameters,
			IDnlibDef def) {
			//
		}

		public void PostRename(IConfuserContext context, INameService service, IProtectionParameters parameters,
			IDnlibDef def) {
			//
		}
	}
}
