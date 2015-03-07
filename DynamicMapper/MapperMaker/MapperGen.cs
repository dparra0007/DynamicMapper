﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.CodeDom;
using DynamicMapper.Interfaces;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Reflection;
using System.IO;
using System.Diagnostics;

namespace DynamicMapper.MapperMaker
{
    public class MapperGen<TInput, TOutput>
    {
        private Type _inputType = typeof(TInput);
        private Type _outputType = typeof(TOutput);
        
        public ITypeMapper<TInput, TOutput> Make()
        {
            var name = LinkNames(_inputType.Name, _outputType.Name);
            var codeUnit = CreateCode(typeof(ITypeMapper<TInput, TOutput>), name);
            var compileResult = CompileCode(_inputType, _outputType, codeUnit, name);            
            var unTypedInstance = InstanciateGeneratedClass(compileResult.CompiledAssembly, name);
            return unTypedInstance as ITypeMapper<TInput, TOutput>;            
        }

        private CodeCompileUnit CreateCode(Type MapperType, string name)
        {
            var codeUnit = new CodeCompileUnit();
            var codeNamespace = new CodeNamespace("DynamicMapper.OnTheFly");
            codeUnit.Namespaces.Add(codeNamespace);
            var interfacesImport = new CodeNamespaceImport("DynamicMapper.Interfaces");
            codeNamespace.Imports.Add(interfacesImport);

            var codeClass = new CodeTypeDeclaration(name);
            
            codeClass.TypeParameters.Add(new CodeTypeParameter("TInput"));
            codeClass.TypeParameters.Add(new CodeTypeParameter("TOutput"));
            codeNamespace.Types.Add(codeClass);

            var typeMapperInterface = new CodeTypeReference(MapperType);
            codeClass.BaseTypes.Add(typeMapperInterface);

            var mapMethod = MakeMethod(_inputType, _outputType, new CodeParameterDeclarationExpression(new CodeTypeReference(_inputType), "entry"));
            mapMethod.Name = "Map";
            codeClass.Members.Add(mapMethod);

            var reverseMapMethod = MakeMethod(_outputType, _inputType, new CodeParameterDeclarationExpression(new CodeTypeReference(_outputType), "entry"));
            reverseMapMethod.Name = "ReverseMap";
            codeClass.Members.Add(reverseMapMethod);           
                        
            return codeUnit;
        }

        private List<string> ExtractAssemblyNamespaces(Assembly assembly)
        {
            var namespaceList = new List<string>();
            var types = assembly.GetTypes();
            foreach (var type in types)
                namespaceList.Add(type.Namespace);
            if (!namespaceList.Any())
                return namespaceList;
            return namespaceList.Distinct().ToList();
        }

        private string LinkNames(string inputName, string outputName)
        {
            return String.Format("{0}To{1}", inputName, outputName);
        }

        private CodeMemberMethod MakeMethod(Type input, Type output, CodeParameterDeclarationExpression entryParamExpression)
        {
            
            var method = new CodeMemberMethod();
            method.Parameters.Add(entryParamExpression);
            var entryParamReference = new CodeVariableReferenceExpression("entry");
            var _outputTypeReference = new CodeTypeReference(output);
            method.ReturnType = _outputTypeReference;
            method.Attributes = MemberAttributes.Public;

            var inputProperties = input.GetProperties();
            var outputProperties = output.GetProperties();

            //Symetric Parsing          
                        
            method.Statements.Add(new CodeVariableDeclarationStatement(output, "output"));
            var outputVarRef = new CodeVariableReferenceExpression("output");

            var createInstaceExpression = new CodeMethodReferenceExpression(new CodeTypeReferenceExpression(typeof(Activator)), "CreateInstance");
            createInstaceExpression.TypeArguments.Add(_outputTypeReference);
            var createInstanceCall = new CodeMethodInvokeExpression(createInstaceExpression);
            method.Statements.Add(new CodeAssignStatement(outputVarRef, createInstanceCall));   

            foreach (var pr in inputProperties)
            {    
                var propertyAssignStatement = new CodeAssignStatement(
                    new CodePropertyReferenceExpression(outputVarRef, pr.Name),
                    new CodePropertyReferenceExpression(entryParamReference, pr.Name));
                method.Statements.Add(propertyAssignStatement);
            }
            var returnStatement = new CodeMethodReturnStatement(outputVarRef);
            method.Statements.Add(returnStatement);            
            
            //TODO Asymetric Parsing                 

            return method;
        }

        #region Compile

        private CompilerResults CompileCode(Type _inputType, Type _outputType, CodeCompileUnit codeUnit, string name)
        {
            var provider = new CSharpCodeProvider();
            var options = new CompilerParameters();

            options.GenerateExecutable = false;
            options.GenerateInMemory = true;
#if DEBUG
            options.IncludeDebugInformation = true;
            string debugSourceFile = name + "DebugCodeFile.cs";
            var codeOptions = new CodeGeneratorOptions();            
            var outputWriter = new IndentedTextWriter(new StreamWriter(debugSourceFile, false), "   ");
            provider.GenerateCodeFromCompileUnit(codeUnit, outputWriter, codeOptions);
            outputWriter.Close();

#else
            options.IncludeDebugInformation = false;
#endif
            options.OutputAssembly = name + "Mapper";
            options.ReferencedAssemblies.Add(this.GetType().Assembly.GetName().Name + ".dll");
            var inputAssemblyName = _inputType.Assembly.GetName().Name + ".dll";
            var outputAssemblyName = _outputType.Assembly.GetName().Name + ".dll";
            options.ReferencedAssemblies.Add(inputAssemblyName);
            if (!inputAssemblyName.Equals(outputAssemblyName))
                options.ReferencedAssemblies.Add(outputAssemblyName);  

            var compileResult = provider.CompileAssemblyFromDom(options, codeUnit);
            if (compileResult.Errors.HasErrors)
                throw new Exception();
            return compileResult;
        }

        private object InstanciateGeneratedClass(Assembly generatedAssembly, string name)
        {
            Type targetType = null;
            foreach (var type in generatedAssembly.GetTypes())
            {
                if (type.Name.Contains(name))
                    targetType = type;
            }
            if (targetType == null)
                throw new Exception();
            var instance = Activator.CreateInstance(targetType.MakeGenericType(_inputType, _outputType));
            return instance;
        }

        #endregion

       
    }
}
