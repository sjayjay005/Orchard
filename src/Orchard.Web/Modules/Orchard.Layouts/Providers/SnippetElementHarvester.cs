﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orchard.DisplayManagement;
using Orchard.DisplayManagement.Descriptors;
using Orchard.Environment;
using Orchard.Environment.Extensions;
using Orchard.Layouts.Elements;
using Orchard.Layouts.Framework.Display;
using Orchard.Layouts.Framework.Drivers;
using Orchard.Layouts.Framework.Elements;
using Orchard.Layouts.Framework.Harvesters;
using Orchard.Layouts.Helpers;
using Orchard.Layouts.Models;
using Orchard.Layouts.Services;
using Orchard.Layouts.Shapes;
using Orchard.Layouts.ViewModels;
using Orchard.Localization;
using Orchard.Themes.Services;
using Orchard.Tokens;
using Orchard.Utility.Extensions;

namespace Orchard.Layouts.Providers {
    [OrchardFeature("Orchard.Layouts.Snippets")]
    public class SnippetElementHarvester : Component, IElementHarvester {
        private const string SnippetShapeSuffix = "Snippet";
        private readonly Work<IShapeFactory> _shapeFactory;
        private readonly Work<ISiteThemeService> _siteThemeService;
        private readonly Work<IShapeTableLocator> _shapeTableLocator;
        private readonly Work<IElementFactory> _elementFactory;
        private readonly Work<IShapeDisplay> _shapeDisplay;
        private readonly Work<ICurrentThemeShapeBindingResolver> _currentThemeShapeBindingResolver;
        private readonly Work<ITokenizer> _tokenizer;

        public SnippetElementHarvester(
            IWorkContextAccessor workContextAccessor,
            Work<IShapeFactory> shapeFactory,
            Work<ISiteThemeService> siteThemeService,
            Work<IShapeTableLocator> shapeTableLocator, 
            Work<IElementFactory> elementFactory,
            Work<IShapeDisplay> shapeDisplay,
            Work<ITokenizer> tokenizer,
            Work<ICurrentThemeShapeBindingResolver> currentThemeShapeBindingResolver) {

            _shapeFactory = shapeFactory;
            _siteThemeService = siteThemeService;
            _shapeTableLocator = shapeTableLocator;
            _elementFactory = elementFactory;
            _shapeDisplay = shapeDisplay;
            _tokenizer = tokenizer;
            _currentThemeShapeBindingResolver = currentThemeShapeBindingResolver;
            workContextAccessor.GetContext();
        }

        public IEnumerable<ElementDescriptor> HarvestElements(HarvestElementsContext context) {
            var currentThemeName = _siteThemeService.Value.GetCurrentThemeName();
            var shapeTable = _shapeTableLocator.Value.Lookup(currentThemeName);
            var shapeDescriptors = shapeTable.Bindings.Where(x => !String.Equals(x.Key, "Elements_Snippet", StringComparison.OrdinalIgnoreCase) && x.Key.EndsWith(SnippetShapeSuffix, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value.ShapeDescriptor);
            var elementType = typeof (Snippet);
            var snippetElement = (Snippet)_elementFactory.Value.Activate(elementType);

            foreach (var shapeDescriptor in shapeDescriptors) {
                var shapeType = shapeDescriptor.Value.ShapeType;
                var snippetDescriptor = DescribeSnippet(shapeType, snippetElement);
                var elementName = GetDisplayName(shapeDescriptor.Value.BindingSource);
                var closureDescriptor = shapeDescriptor;
                yield return new ElementDescriptor(elementType, shapeType, T(elementName), T("An element that renders the {0} shape.", shapeType), snippetElement.Category) {
                    Displaying = displayContext => Displaying(displayContext, closureDescriptor.Value, snippetDescriptor),
                    ToolboxIcon = "\uf10c",
                    EnableEditorDialog = snippetDescriptor.Fields.Any(),
                    Editor = ctx => Editor(snippetDescriptor, ctx),
                    UpdateEditor = ctx => UpdateEditor(snippetDescriptor, ctx)
                };
            }
        }

        private void Editor(SnippetDescriptor descriptor, ElementEditorContext context) {
            UpdateEditor(descriptor, context);
        }

        private void UpdateEditor(SnippetDescriptor descriptor, ElementEditorContext context) {
            var viewModel = new SnippetViewModel {
                Descriptor = descriptor
            };
            
            if (context.Updater != null) {
                foreach (var fieldDescriptor in descriptor.Fields) {
                    var name = fieldDescriptor.Name;
                    var result = context.ValueProvider.GetValue(name);

                    if (result == null)
                        continue;

                    context.Element.Data[name] = result.AttemptedValue;
                }
            }

            viewModel.FieldEditors = descriptor.Fields.Select(x => {
                var fieldEditorTemplateName = String.Format("Elements.Snippet.Field.{0}", x.Type ?? "Text");
                var fieldDescriptorViewModel = new SnippetFieldViewModel {
                    Descriptor = x,
                    Value = context.Element.Data.Get(x.Name)
                };
                var fieldEditor = context.ShapeFactory.EditorTemplate(TemplateName: fieldEditorTemplateName, Model: fieldDescriptorViewModel, Prefix: context.Prefix);

                return fieldEditor;
            }).ToList();

            var snippetEditorShape = context.ShapeFactory.EditorTemplate(TemplateName: "Elements.Snippet", Model: viewModel, Prefix: context.Prefix);
            snippetEditorShape.Metadata.Position = "Fields:0";
            
            context.EditorResult.Add(snippetEditorShape);
        }

        private void Displaying(ElementDisplayingContext context, ShapeDescriptor shapeDescriptor, SnippetDescriptor snippetDescriptor) {
            var shapeType = shapeDescriptor.ShapeType;
            var shape = (dynamic)_shapeFactory.Value.Create(shapeType);

            shape.Element = context.Element;
            shape.SnippetDescriptor = snippetDescriptor;

            ElementShapes.AddTokenizers(shape, _tokenizer.Value);
            context.ElementShape.Snippet = shape;
            context.ElementShape.SnippetDescriptor = snippetDescriptor;
        }

        private string GetDisplayName(string bindingSource) {
            var fileName = Path.GetFileNameWithoutExtension(bindingSource);
            var lastIndex = fileName.IndexOf(SnippetShapeSuffix, StringComparison.OrdinalIgnoreCase);
            return fileName.Substring(0, lastIndex).CamelFriendly();
        }

        private SnippetDescriptor DescribeSnippet(string shapeType, Snippet element) {
            var shape = (dynamic)_shapeFactory.Value.Create(shapeType);
            shape.Element = element;
            return DescribeSnippet(shape);
        }

        private SnippetDescriptor DescribeSnippet(dynamic shape) {
            // Execute the shape and intercept calls to the Html.SnippetField method.
            var descriptor = new SnippetDescriptor();
            shape.DescriptorRegistrationCallback = (Action<SnippetFieldDescriptor>) (fieldDescriptor => {
                descriptor.Fields.Add(fieldDescriptor);

                if (fieldDescriptor.DisplayName == null)
                    fieldDescriptor.DisplayName = new LocalizedString(fieldDescriptor.Name);
            });

            using (_currentThemeShapeBindingResolver.Value.Enable()) {
                _shapeDisplay.Value.Display(shape);
            }

            shape.SnippetDescriptor = descriptor;
            return descriptor;
        }
    }
}