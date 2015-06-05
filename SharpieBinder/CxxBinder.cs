﻿//
// CURRENTLY
//    Generate base classes without a call to base
//    Generate the standard idioms/patterns for init, dispose
//
// WARNING
//
//    This file still contains code from the original CppBind, it is some legacy code
//    so it might be possible to remove.
//
//TODO:
//  operators
// 
// StringHash is jsut a "struct StringHash { int value; }", so it might make sense
// to avoid binding it, and manually handle it instead.
//
// The API uses "StringHash&" as a return type extensively, it might be worth
// creating a StringHashRef struct perhaps that is a "struct StringHashRef { IntPtr value }" and
// that is implicitly convertible to a StringHash by dereferencing the value
//

using System;
using System.Collections.Generic;
using Clang.Ast;
using ICSharpCode.NRefactory.CSharp;
using System.Linq;
using Attribute = ICSharpCode.NRefactory.CSharp.Attribute;
using System.Reflection;
using Sharpie.Bind;


namespace SharpieBinder
{
	class CxxBinder : AstVisitor
	{
		CSharpParser csParser = new CSharpParser();
		public static Clang.Ast.Type CleanType(QualType qt)
		{
			var et = qt.Type as ElaboratedType;
			if (et == null)
				return qt.Type;
			return et.UnqualifiedDesugaredType;
		}

		class BaseNodeType
		{
			public CXXRecordDecl Decl { get; set; }

			readonly Action<CXXRecordDecl, CXXRecordDecl> bindHandler;

			public BaseNodeType(Action<CXXRecordDecl, CXXRecordDecl> bindHandler)
			{
				this.bindHandler = bindHandler;
			}

			public void Bind()
			{
				bindHandler(null, Decl);
			}

			public void Bind(CXXRecordDecl decl)
			{
				bindHandler(Decl, decl);
			}
		}

		readonly List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
		readonly Dictionary<string, BaseNodeType> baseNodeTypes;

		TypeDeclaration astVisitorType;
		TypeDeclaration currentType;

		public CxxBinder()
		{
			baseNodeTypes = new Dictionary<string, BaseNodeType>
			{
				["Urho3D::Object"] = new BaseNodeType(Bind),
				["Urho3D::RefCounted"] = new BaseNodeType(Bind),
				["Urho3D::RefCount"] = new BaseNodeType(Bind),
				//["clang::DeclContext"] = new BaseNodeType(Bind),
				//["clang::Decl"] = new BaseNodeType(Bind),
				//["clang::Type"] = new BaseNodeType(Bind),
				//["clang::Attr"] = new BaseNodeType(Bind),
				//["clang::Stmt"] = new BaseNodeType(Bind)
			};
		}

		public IEnumerable<SyntaxTree> Generate()
		{
			foreach (var syntaxTree in syntaxTrees) {
				//syntaxTree.AcceptVisitor(new Sharpie.Bind.Massagers.GenerateUsingStatementsMassager());

				yield return syntaxTree;
			}
		}

		CXXRecordDecl GetRootDecl(CXXRecordDecl decl)
		{
			return baseNodeTypes.Values.FirstOrDefault(node =>
								   node.Decl != null &&
								   node.Decl.Name != "DeclContext" &&
								   decl.IsDerivedFrom(node.Decl))?.Decl;
		}

		void PushType(TypeDeclaration typeDeclaration)
		{
			var syntaxTree = new SyntaxTree
			{
				FileName = typeDeclaration.Name + ".cs"
			};
			Console.WriteLine(syntaxTree);

			var headerLines = String.Format(
				"WARNING - AUTOGENERATED - DO NOT EDIT\n\n" +
				"Generated using `sharpie urho`\n\n" +
				"{0}\n\n" +
				"Copyright 2015 Xamarin Inc. All rights reserved.", syntaxTree.FileName
			).Split('\n');
			headerLines[headerLines.Length - 1] += "\n";

			foreach (var line in headerLines)
				syntaxTree.Members.Add(new Comment(" " + line));

			syntaxTree.Members.Add(new UsingDeclaration("System"));
			syntaxTree.Members.Add (new UsingDeclaration () { Import = new SimpleType ("System.Runtime.InteropServices") });

			var ns = new NamespaceDeclaration("Urho");
			syntaxTree.Members.Add(ns);
			syntaxTrees.Add(syntaxTree);

			ns.Members.Add(currentType = typeDeclaration);
		}

		public override void VisitEnumDecl(EnumDecl decl, VisitKind visitKind)
		{
			if (visitKind != VisitKind.Enter || !decl.IsCompleteDefinition || decl.QualifiedName == null)
				return;

			Console.WriteLine($"--here: {decl.QualifiedName}");
			string typeName = decl.Name;

			PushType(new TypeDeclaration
			{
				Name = typeName,
				ClassType = ClassType.Enum,
				Modifiers = Modifiers.Public
			});

			foreach (var constantDecl in decl.Decls<EnumConstantDecl>()) {
				var valueName = constantDecl.Name;

				switch (valueName) {
					// LIST HERE ANY Values we want to skip
				case "foo":
					continue;
				
				}

				currentType.Members.Add(new EnumMemberDeclaration { Name = valueName });
			}
		}

		public override void VisitCXXRecordDecl(CXXRecordDecl decl, VisitKind visitKind)
		{
			if (visitKind != VisitKind.Enter || !decl.IsCompleteDefinition || decl.Name == null) {
				return;
			}
			if (visitKind == VisitKind.Leave)
				currentType = null;

			BaseNodeType baseNodeType;
			if (baseNodeTypes.TryGetValue(decl.QualifiedName, out baseNodeType)) {
				baseNodeType.Decl = decl;
				baseNodeType.Bind();
				return;
			}

			switch (decl.QualifiedName) {
				case "clang::ObjCObjectTypeImpl":
					// from the docs: "Code outside of ASTContext and the
					// core type system should not reference this type."
					return;
			}

			foreach (var bnt in baseNodeTypes.Values) {
				if (bnt.Decl != null && decl.IsDerivedFrom(bnt.Decl)) {
					bnt.Bind(decl);
					return;
				}
			}
		}

		//
		// Determines if this is a structure that is part of native interop, so we would
		// keep the layout the same as managed code
		//
		public bool IsStructure(CXXRecordDecl decl)
		{
			// Quick and dirty: it really should use our manually curated list of known value types,
			// but for now, this will do
			if (decl.TagKind == TagDeclKind.Struct || !decl.IsDerivedFrom(ScanBaseTypes.UrhoRefCounted))
				return true;
			return false;
		}

		void Bind(CXXRecordDecl baseDecl, CXXRecordDecl decl)
		{
			var name = decl.Name;
			Console.WriteLine(name);

			PushType(new TypeDeclaration
			{
				Name = decl.Name,
				ClassType = decl.TagKind == TagDeclKind.Struct ? ClassType.Struct : ClassType.Class,
				Modifiers = Modifiers.Partial | Modifiers.Public | Modifiers.Unsafe
			});

			if (baseDecl != null) {
				foreach (var baseType in decl.Bases) {
					var baseName = baseType.Decl?.Name;
					if (baseName == null)
						continue;

					if (currentType.BaseTypes.Count > 0)
						baseName = "I" + baseName;

					currentType.BaseTypes.Add(new SimpleType(baseName));
				}
			}

			// Determines if this is a subclass of RefCounted (but not RefCounted itself)
			bool refCountedSubclass = decl.TagKind == TagDeclKind.Class && decl.QualifiedName != "Urho3D::RefCounted" && decl.IsDerivedFrom (ScanBaseTypes.UrhoRefCounted);

			if (refCountedSubclass) {
				var nativeCtor = new ConstructorDeclaration
				{
					Modifiers = Modifiers.Internal,
					Body = new BlockStatement(),
					Initializer = new ConstructorInitializer()
				};


				nativeCtor.Parameters.Add(new ParameterDeclaration(new SimpleType("IntPtr"), "handle"));
				nativeCtor.Initializer.Arguments.Add(new IdentifierExpression("handle"));

				currentType.Members.Add(nativeCtor);
			} else if (IsStructure(decl)) {
				var serializable = new Attribute()
				{
					Type = new SimpleType("StructLayout")
				};

				serializable.Arguments.Add(new TypeReferenceExpression(new MemberType(new SimpleType("LayoutKind"), "Sequential")));
				var attrs = new AttributeSection();
				attrs.Attributes.Add(serializable);
				currentType.Attributes.Add(attrs);
			}
		}

		public static AstType CreateAstType(string dottedName, IEnumerable<AstType> typeArguments)
		{
			var parts = dottedName.Split('.');
			if (parts.Length == 1)
				return new SimpleType(dottedName, typeArguments);

			AstType type = new SimpleType(parts[0]);

			for (int i = 1; i < parts.Length; i++) {
				if (i < parts.Length - 1)
					type = new MemberType(type, parts[i]);
				else
					type = new MemberType(type, parts[i], typeArguments);
			}

			return type;
		}

		AstType GenerateReflectedType(System.Type type)
		{
			if (type == typeof(void))
				return new PrimitiveType("void");

			var name = type.FullName;
			if (type.IsGenericType)
				name = name.Substring(0, name.IndexOf('`'));

			return CreateAstType(name, type.GetGenericArguments().Select(at => GenerateReflectedType(at)));
		}

		static string MakeName(string typeName)
		{
			return typeName;
		}

		// Temporary, just to help us get the bindings bootstrapped
		static bool IsUnsupportedType(QualType qt)
		{
			var s = CleanType(qt).Bind().ToString();
			if (s.IndexOf("unsupported") != -1)
				return true;

			// Quick hack, just while we get thigns going
			if (s.IndexOf("EventHandler**") != -1) {
				return true;
			}
			return false;
		}

		// 
		// Given a pointer type, returns the underlying type 
		//
		static RecordType GetPointersUnderlyingType (PointerType ptrType)
		{
			return (ptrType.PointeeQualType.Type.UnqualifiedDesugaredType as RecordType);
		}

		enum WrapKind
		{
			None,
			RefCounted,
			EventHandler
		}
		// 
		// Given a Clang QualType, returns the AstType to use to marshal, both for the 
		// P/Invoke signature and for the high level binding
		//
		// Handles RefCounted objects that we wrap
		//
		void LookupMarshalTypes (QualType qt, out AstType lowLevel, out AstType highLevel, out WrapKind wrapKind, bool isReturn = false)
		{
			wrapKind = WrapKind.None;

			var cleanType = CleanType (qt);
			var cleanTypeStr = cleanType.ToString();
			switch (cleanTypeStr) {
				case "const char *":
					lowLevel = new PrimitiveType("string");
					highLevel = new PrimitiveType("string");
					return;
				case "ThreadID":
					// Troublesome because on windows it is a sizeof(unsigned), on unix sizeof(pthread_t), which is 
					// 32 or 64 bits.
				case "void *":
					lowLevel = new PrimitiveType("IntPtr");
					highLevel = new PrimitiveType("IntPtr");
					return;
			}

			if (cleanType.Kind == TypeKind.Pointer) {
				var ptrType = cleanType as PointerType;
				var underlying = GetPointersUnderlyingType (ptrType);

				CXXRecordDecl decl;
				if (underlying != null && ScanBaseTypes.nameToDecl.TryGetValue (underlying.Decl.QualifiedName, out decl)) {
					if (decl.IsDerivedFrom (ScanBaseTypes.UrhoRefCounted)) {
						lowLevel = new SimpleType ("IntPtr");

						if (decl.QualifiedName == "Urho3D::Object")
							highLevel = csParser.ParseTypeReference("Urho.Object");
						else 
							highLevel = underlying.Bind ();
						wrapKind = WrapKind.RefCounted;
						return;
					}
					if (decl == ScanBaseTypes.EventHandlerType) {
						wrapKind = WrapKind.EventHandler;
						lowLevel = new SimpleType("IntPtr");
						highLevel = new SimpleType("IntPtr");
						return;
					}
					if (decl.Name == "ProfilerBlock") {
						lowLevel = new SimpleType("ProfilerBlock");
						highLevel = new SimpleType("ProfilerBlock");
					}
				}
			}
			lowLevel = cleanType.Bind ();
			highLevel = cleanType.Bind();
		}

		public override void VisitCXXMethodDecl(CXXMethodDecl decl, VisitKind visitKind){
			// Global definitions, not inside a class, skip
			if (currentType == null) {
				return;
			}

			if (visitKind != VisitKind.Enter)
				return;

			if (decl is CXXConstructorDecl || decl is CXXDestructorDecl)
				return;


			if (decl.IsCopyAssignmentOperator || decl.IsMoveAssignmentOperator)
				return;

			// TODO: temporary, do not handle opreators
			if (decl.Name.StartsWith("operator", StringComparison.Ordinal))
				return;

			if (decl.Parent.Name != currentType.Name) {
				Console.WriteLine("For some reason we go t amethod that does not belong here {0}.{1} on {2}", decl.Parent.Name, decl.Name, currentType.Name);
				return;
			}

			// Temporary: while we add support for other things, just to debug things
			// remove types we do not support
			foreach (var p in decl.Parameters) {
				if (IsUnsupportedType (p.QualType)){
					Console.WriteLine($"Bailing out on {p.QualType}");
					return;
				}
			}
			if (IsUnsupportedType(decl.ReturnQualType)) {
				Console.WriteLine($"Bailing out on {decl.QualType}");
				return;
			}

			AstType pinvokeReturn, methodReturn;
			WrapKind returnIsWrapped;
			LookupMarshalTypes (decl.ReturnQualType, out pinvokeReturn, out methodReturn, out returnIsWrapped, isReturn: true);
			var methodReturn2 = methodReturn.Clone();
			// PInvoke declaration
			string pinvoke_name = MakeName(currentType.Name) + "_" + decl.Name;
			var pinvoke = new MethodDeclaration
			{
				Name = pinvoke_name,
				ReturnType = pinvokeReturn,
				Modifiers = Modifiers.Extern | Modifiers.Static | Modifiers.Internal
			};
			if (!decl.IsStatic)
				pinvoke.Parameters.Add(new ParameterDeclaration(new SimpleType("IntPtr"), "handle"));


			var dllImport = new Attribute()
			{
				Type = new SimpleType("DllImport")
			};
			dllImport.Arguments.Add(new PrimitiveExpression("mono-urho"));

			pinvoke.Attributes.Add(new AttributeSection(dllImport));
			currentType.Members.Add(pinvoke);

			// Method declaration
			var method = new MethodDeclaration
			{
				Name = decl.Name,
				ReturnType = methodReturn,
				Modifiers = (decl.IsStatic ? Modifiers.Static : 0) | Modifiers.Public
			};
			method.Body = new BlockStatement();

			var invoke = new InvocationExpression(new IdentifierExpression(pinvoke_name));
			if (!decl.IsStatic)
				invoke.Arguments.Add(new IdentifierExpression("handle"));
            foreach (var param in decl.Parameters) {
				AstType pinvokeParameter, parameter;
				WrapKind wrapKind;

				LookupMarshalTypes(param.QualType, out pinvokeParameter, out parameter, out wrapKind);

				method.Parameters.Add(new ParameterDeclaration(parameter, param.Name));
				pinvoke.Parameters.Add(new ParameterDeclaration(pinvokeParameter, param.Name));

				if (wrapKind == WrapKind.RefCounted) {
					var cond = new ConditionalExpression(new BinaryOperatorExpression(new IdentifierExpression(param.Name), BinaryOperatorType.Equality, new PrimitiveExpression(null)),
					                                     csParser.ParseExpression("IntPtr.Zero"), csParser.ParseExpression(param.Name + ".handle"));

					invoke.Arguments.Add(cond);
				} else {
					invoke.Arguments.Add(new IdentifierExpression(param.Name));
				}
			}
			if (methodReturn is Sharpie.Bind.Types.VoidType)
				method.Body.Add(invoke);
			else {
				var ret = new ReturnStatement();
				if (returnIsWrapped == WrapKind.RefCounted) {
					
					//var id = new IdentifierExpression("LookupObject");
					//id.TypeArguments.Add(methodReturn2);

					//ret.Expression = new InvocationExpression(id, invoke);
					ret.Expression = new ObjectCreateExpression(methodReturn2, new IdentifierExpression("handle"));
				} else if (returnIsWrapped == WrapKind.EventHandler){
					ret.Expression = invoke;
				} else {
					ret.Expression = invoke;
				}
				method.Body.Add(ret);
			}

			currentType.Members.Add(method);
		}
	}

	class ScanBaseTypes : AstVisitor
	{
		static public CXXRecordDecl UrhoRefCounted, EventHandlerType;
		public static Dictionary<string,CXXRecordDecl> nameToDecl = new Dictionary<string, CXXRecordDecl>();

		public override void VisitCXXRecordDecl(CXXRecordDecl decl, VisitKind visitKind)
		{
			if (visitKind != VisitKind.Enter || !decl.IsCompleteDefinition || decl.Name == null) 
				return;
		
			nameToDecl [decl.QualifiedName] = decl;
			switch (decl.QualifiedName) {
			case "Urho3D::RefCounted":
				UrhoRefCounted = decl;
				break;
			case "Urho3D::EventHandler":
					EventHandlerType = decl;
					break;
			}
		}
	}
}
