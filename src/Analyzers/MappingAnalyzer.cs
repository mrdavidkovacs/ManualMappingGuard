using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ManualMappingGuard.Analyzers.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace ManualMappingGuard.Analyzers
{
  [DiagnosticAnalyzer(LanguageNames.CSharp)]
  public class MappingAnalyzer : DiagnosticAnalyzer
  {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
      ImmutableArray.Create(Diagnostics.UnmappedProperty, Diagnostics.MissingMappingTargetType);

    public override void Initialize(AnalysisContext context)
    {
      if (context == null)
        throw new ArgumentNullException(nameof(context));

      context.EnableConcurrentExecution();
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
      context.RegisterSyntaxNodeAction(OnMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private void OnMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
      var method = (IMethodSymbol) context.ContainingSymbol;
      if (!method.IsMappingMethod(context.Compilation))
        return;

      var methodDeclarationSyntax = (MethodDeclarationSyntax) context.Node;
      var mappingMethodAttributeSyntax = MappingMethodDetection
        .GetMappingMethodAttributeSyntax(methodDeclarationSyntax, context.SemanticModel);

      var location = mappingMethodAttributeSyntax?.GetLocation() ?? methodDeclarationSyntax.Identifier.GetLocation();

      var mappingTargetType = method.GetMappingTargetType(context.Compilation);
      if (mappingTargetType == null)
      {
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingMappingTargetType, location));
        return;
      }

      var mappingTargetProperties = mappingTargetType.GetMappingTargetProperties();
      var mappedProperties = new HashSet<IPropertySymbol>();

      foreach (var assignment in context.Node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
      {
        var assignmentTarget = context.SemanticModel.GetSymbolInfo(assignment.Left);
        if (assignmentTarget.Symbol is IPropertySymbol targetProperty)
          mappedProperties.Add(targetProperty);
      }

      var excludedPropertyNames = method.GetAttributes()
        .Where(a => a.AttributeClass.InheritsFromOrEquals(context.Compilation.GetExistingType<UnmappedPropertiesAttribute>()))
        .SelectMany(a => this.ExtractPropertyNamesFromAttribute(a, context.Compilation))
        .ToList();

      var unmappedPropertyNames = mappingTargetProperties
        .Except(mappedProperties, new RootPropertyEqualityComparer())
        .Select(p => p.Name)
        .Except(excludedPropertyNames)
        .OrderBy(n => n);

      foreach (var unmappedPropertyName in unmappedPropertyNames)
      {
        var diagnostic = Diagnostic.Create(Diagnostics.UnmappedProperty, location, unmappedPropertyName);
        context.ReportDiagnostic(diagnostic);
      }
    }

    private IEnumerable<string> ExtractPropertyNamesFromAttribute(AttributeData att, Compilation compilation)
    {
      var symbolDisplayFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
      string fullyQualifiedName = att.AttributeClass.ToDisplayString(symbolDisplayFormat);
      string sourceText = att.ApplicationSyntaxReference.SyntaxTree.GetText().GetSubText(att.ApplicationSyntaxReference.Span).ToString();

      StringBuilder sb = new StringBuilder();

      int indexOfBracket = sourceText.IndexOf("(", StringComparison.Ordinal);

      if (indexOfBracket < 0)
      {
        sb.Append(fullyQualifiedName)
          .Append("()");
      }
      else
      {
        sb.Append(fullyQualifiedName)
          .Append(sourceText.Substring(indexOfBracket));
      }

      string formattableString = $@"
        using System;
        using System.Collections.Generic;

        namespace Test
        {{
          public class Tester
          {{
            public static void Main(string[] args)
            {{
            }}

            public IEnumerable<string> TestMe()
            {{
              return new {sb.ToString()}.PropertyNames;
            }}
          }}
        }}";
      
      CSharpCompilation temp = CSharpCompilation.Create(Guid.NewGuid().ToString("N"))
        .AddSyntaxTrees(att.ApplicationSyntaxReference.SyntaxTree)
        .AddSyntaxTrees(CSharpSyntaxTree.ParseText(formattableString))
        .AddReferences(compilation.References);

      using (var ms = new MemoryStream())
      {
        EmitResult result = temp.Emit(ms);

        if (!result.Success)
        {
          IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => 
            diagnostic.IsWarningAsError || 
            diagnostic.Severity == DiagnosticSeverity.Error);

          foreach (Diagnostic diagnostic in failures)
          {
            Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
          }

          return new List<string>();
        }

        ms.Seek(0, SeekOrigin.Begin);
        Assembly assembly = Assembly.Load(ms.ToArray());

        Type type = assembly.GetType("Test.Tester");

        var obj = Activator.CreateInstance(type);
        List<string> x = ((IEnumerable<string>) type.InvokeMember("TestMe",
          BindingFlags.Default | BindingFlags.InvokeMethod,
          null, obj, null)).ToList();

        return x;
      }
    }
  }
}
