using System.Reflection;
using System;
using System.Collections.Generic;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Utilities;
using HotChocolate.Configuration;
using HotChocolate.Configuration.Bindings;
using HotChocolate.Internal;
using HotChocolate.Properties;

namespace HotChocolate
{
    public partial class SchemaBuilder
        : ISchemaBuilder
    {
        private delegate ITypeReference CreateRef(ITypeInspector typeInspector);

        private readonly Dictionary<string, object> _contextData =
            new Dictionary<string, object>();
        private readonly List<FieldMiddleware> _globalComponents =
            new List<FieldMiddleware>();
        private readonly List<LoadSchemaDocument> _documents =
            new List<LoadSchemaDocument>();
        private readonly List<CreateRef> _types = new List<CreateRef>();
        private readonly List<Type> _resolverTypes = new List<Type>();
        private readonly Dictionary<OperationType, CreateRef> _operations =
            new Dictionary<OperationType, CreateRef>();
        private readonly Dictionary<FieldReference, FieldResolver> _resolvers =
            new Dictionary<FieldReference, FieldResolver>();
        private readonly Dictionary<(Type, string), CreateConvention> _conventions =
            new Dictionary<(Type, string), CreateConvention>();
        private readonly Dictionary<Type, (CreateRef, CreateRef)> _clrTypes =
            new Dictionary<Type, (CreateRef, CreateRef)>();
        private readonly List<object> _interceptors = new List<object>();
        private readonly List<Action<IDescriptorContext>> _onBeforeCreate =
            new List<Action<IDescriptorContext>>();
        private readonly IBindingCompiler _bindingCompiler =
            new BindingCompiler();
        private SchemaOptions _options = new SchemaOptions();
        private IsOfTypeFallback _isOfType;
        private IServiceProvider _services;
        private CreateRef _schema;

        public ISchemaBuilder SetSchema(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (typeof(Schema).IsAssignableFrom(type))
            {
                _schema = ti => ti.GetTypeRef(type);
            }
            else
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_SchemaTypeInvalid,
                    nameof(type));
            }
            return this;
        }

        public ISchemaBuilder SetSchema(ISchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (schema is TypeSystemObjectBase)
            {
                _schema = ti => new SchemaTypeReference(schema);
            }
            else
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_ISchemaNotTso,
                    nameof(schema));
            }
            return this;
        }

        public ISchemaBuilder SetSchema(Action<ISchemaTypeDescriptor> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            _schema = ti => new SchemaTypeReference(new Schema(configure));
            return this;
        }

        public ISchemaBuilder SetOptions(IReadOnlySchemaOptions options)
        {
            if (options != null)
            {
                _options = SchemaOptions.FromOptions(options);
            }
            return this;
        }

        public ISchemaBuilder ModifyOptions(Action<ISchemaOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            configure(_options);
            return this;
        }

        public ISchemaBuilder Use(FieldMiddleware middleware)
        {
            if (middleware == null)
            {
                throw new ArgumentNullException(nameof(middleware));
            }

            _globalComponents.Add(middleware);
            return this;
        }

        public ISchemaBuilder AddDocument(LoadSchemaDocument loadSchemaDocument)
        {
            if (loadSchemaDocument == null)
            {
                throw new ArgumentNullException(nameof(loadSchemaDocument));
            }

            _documents.Add(loadSchemaDocument);
            return this;
        }

        public ISchemaBuilder AddType(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type.IsDefined(typeof(GraphQLResolverOfAttribute), true))
            {
                AddResolverType(type);
            }
            else
            {
                _types.Add(ti => ti.GetTypeRef(type));
            }

            return this;
        }

        public ISchemaBuilder TryAddConvention(
            Type convention,
            CreateConvention factory,
            string scope = null)
        {
            if (convention is null)
            {
                throw new ArgumentNullException(nameof(convention));
            }

            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (!_conventions.ContainsKey((convention, scope)))
            {
                AddConvention(convention, factory, scope);
            }

            return this;
        }

        public ISchemaBuilder AddConvention(
            Type convention,
            CreateConvention factory,
            string scope = null)
        {
            if (convention is null)
            {
                throw new ArgumentNullException(nameof(convention));
            }

            _conventions[(convention, scope)] = factory ??
                throw new ArgumentNullException(nameof(factory));

            return this;
        }

        public ISchemaBuilder BindClrType(Type runtimeType, Type schemaType)
        {
            if (runtimeType is null)
            {
                throw new ArgumentNullException(nameof(runtimeType));
            }

            if (schemaType is null)
            {
                throw new ArgumentNullException(nameof(schemaType));
            }

            if (!schemaType.IsSchemaType())
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_MustBeSchemaType,
                    nameof(schemaType));
            }

            TypeContext context = SchemaTypeReference.InferTypeContext(schemaType);
            _clrTypes[runtimeType] =
                (ti => ti.GetTypeRef(runtimeType, context),
                ti => ti.GetTypeRef(schemaType, context));

            return this;
        }

        private void AddResolverType(Type type)
        {
            GraphQLResolverOfAttribute attribute =
                type.GetCustomAttribute<GraphQLResolverOfAttribute>(true);

            _resolverTypes.Add(type);

            if (attribute?.Types != null)
            {
                foreach (Type schemaType in attribute.Types)
                {
                    if (typeof(ObjectType).IsAssignableFrom(schemaType) &&
                        schemaType.IsSchemaType())
                    {
                        _types.Add(ti => ti.GetTypeRef(schemaType));
                    }
                }
            }
        }

        public ISchemaBuilder AddType(INamedType type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _types.Add(ti => TypeReference.Create(type));
            return this;
        }

        public ISchemaBuilder AddType(INamedTypeExtension type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _types.Add(ti => TypeReference.Create(type));
            return this;
        }

        public ISchemaBuilder AddDirectiveType(DirectiveType type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _types.Add(ti => TypeReference.Create(type));
            return this;
        }

        public ISchemaBuilder AddRootType(
            Type type,
            OperationType operation)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!type.IsClass)
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_RootType_MustBeClass,
                    nameof(type));
            }

            if (type.IsNonGenericSchemaType())
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_RootType_NonGenericType,
                    nameof(type));
            }

            if (type.IsSchemaType()
                && !typeof(ObjectType).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_RootType_MustBeObjectType,
                    nameof(type));
            }

            if (_operations.ContainsKey(operation))
            {
                throw new ArgumentException(
                    string.Format("The root type `{0}` has already been registered.", operation),
                    nameof(operation));
            }

            _operations.Add(operation, ti => ti.GetTypeRef(type, TypeContext.Output));
            _types.Add(ti => ti.GetTypeRef(type, TypeContext.Output));
            return this;
        }

        public ISchemaBuilder AddRootType(
            ObjectType type,
            OperationType operation)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_operations.ContainsKey(operation))
            {
                throw new ArgumentException(
                    string.Format("The root type `{0}` has already been registered.", operation),
                    nameof(operation));
            }

            SchemaTypeReference reference = TypeReference.Create(type);
            _operations.Add(operation, ti => reference);
            _types.Add(ti => reference);
            return this;
        }

        public ISchemaBuilder AddResolver(FieldResolver fieldResolver)
        {
            if (fieldResolver == null)
            {
                throw new ArgumentNullException(nameof(fieldResolver));
            }

            _resolvers.Add(fieldResolver.ToFieldReference(), fieldResolver);
            return this;
        }

        public ISchemaBuilder AddBinding(IBindingInfo binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (!binding.IsValid())
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_Binding_Invalid,
                    nameof(binding));
            }

            if (!_bindingCompiler.CanHandle(binding))
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_Binding_CannotBeHandled,
                    nameof(binding));
            }

            _bindingCompiler.AddBinding(binding);
            return this;
        }

        public ISchemaBuilder SetTypeResolver(IsOfTypeFallback isOfType)
        {
            _isOfType = isOfType;
            return this;
        }

        public ISchemaBuilder AddServices(IServiceProvider services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            _services = _services is null ? services : _services.Include(services);

            return this;
        }

        public ISchemaBuilder SetContextData(string key, object value)
        {
            _contextData[key] = value;
            return this;
        }

        public ISchemaBuilder SetContextData(string key, Func<object, object> update)
        {
            _contextData.TryGetValue(key, out object value);
            _contextData[key] = update(value);
            return this;
        }

        public ISchemaBuilder AddTypeInterceptor(Type interceptor)
        {
            if (interceptor is null)
            {
                throw new ArgumentNullException(nameof(interceptor));
            }

            if (!typeof(ITypeInitializationInterceptor).IsAssignableFrom(interceptor))
            {
                throw new ArgumentException(
                    TypeResources.SchemaBuilder_Interceptor_NotSuppported,
                    nameof(interceptor));
            }

            _interceptors.Add(interceptor);
            return this;
        }

        public ISchemaBuilder AddTypeInterceptor(ITypeInitializationInterceptor interceptor)
        {
            if (interceptor is null)
            {
                throw new ArgumentNullException(nameof(interceptor));
            }

            _interceptors.Add(interceptor);
            return this;
        }

        public ISchemaBuilder OnBeforeCreate(
            Action<IDescriptorContext> action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _onBeforeCreate.Add(action);
            return this;
        }

        public static SchemaBuilder New() => new SchemaBuilder();
    }
}
