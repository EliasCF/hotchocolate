using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HotChocolate.Types.Descriptors;
using static HotChocolate.Utilities.ThrowHelper;

#nullable  enable

namespace HotChocolate.Configuration
{
    internal sealed class TypeRegistry
    {
        private readonly Dictionary<ITypeReference, RegisteredType> _typeRegister =
            new Dictionary<ITypeReference, RegisteredType>();
        private readonly Dictionary<ExtendedTypeReference, ITypeReference> _runtimeTypeRefs =
            new Dictionary<ExtendedTypeReference, ITypeReference>(
                new ExtendedTypeReferenceEqualityComparer());
        private readonly Dictionary<NameString, ITypeReference> _nameRefs =
            new Dictionary<NameString, ITypeReference>();
        private List<RegisteredType> _types = new List<RegisteredType>();

        public int Count => _typeRegister.Count;

        public IReadOnlyList<RegisteredType> Types => _types;

        public IReadOnlyDictionary<ExtendedTypeReference, ITypeReference> RuntimeTypeRefs =>
            _runtimeTypeRefs;

        public IReadOnlyDictionary<NameString, ITypeReference> NameRefs => _nameRefs;

        public bool IsRegistered(ITypeReference typeReference)
        {
            if (typeReference is null)
            {
                throw new ArgumentNullException(nameof(typeReference));
            }

            if (_typeRegister.ContainsKey(typeReference))
            {
                return true;
            }

            if (typeReference is ExtendedTypeReference clrTypeReference)
            {
                return _runtimeTypeRefs.ContainsKey(clrTypeReference);
            }

            return false;
        }

        public bool TryGetType(
            ITypeReference typeRef,
            [NotNullWhen(true)] out RegisteredType? registeredType)
        {
            if (typeRef is null)
            {
                throw new ArgumentNullException(nameof(typeRef));
            }

            if (typeRef is ExtendedTypeReference clrTypeRef &&
                _runtimeTypeRefs.TryGetValue(clrTypeRef, out ITypeReference? internalRef))
            {
                typeRef = internalRef;
            }

            return _typeRegister.TryGetValue(typeRef, out registeredType);
        }

        public bool TryGetTypeRef(
            ExtendedTypeReference runtimeTypeRef,
            [NotNullWhen(true)] out ITypeReference? typeRef)
        {
            if (runtimeTypeRef is null)
            {
                throw new ArgumentNullException(nameof(runtimeTypeRef));
            }

            return _runtimeTypeRefs.TryGetValue(runtimeTypeRef, out typeRef);
        }

        public bool TryGetTypeRef(
            NameString typeName,
            [NotNullWhen(true)] out ITypeReference? typeRef)
        {
            typeName.EnsureNotEmpty(nameof(typeName));

            if (!_nameRefs.TryGetValue(typeName, out typeRef))
            {
                typeRef = Types
                    .FirstOrDefault(t => !t.IsExtension && t.Type.Name.Equals(typeName))
                    ?.References[0];
            }
            return typeRef is not null;
        }

        public IEnumerable<ITypeReference> GetTypeRefs() => _runtimeTypeRefs.Values;

        public void TryRegister(ExtendedTypeReference runtimeTypeRef, ITypeReference typeRef)
        {
            if (runtimeTypeRef is null)
            {
                throw new ArgumentNullException(nameof(runtimeTypeRef));
            }

            if (typeRef is null)
            {
                throw new ArgumentNullException(nameof(typeRef));
            }

            if (!_runtimeTypeRefs.ContainsKey(runtimeTypeRef))
            {
                _runtimeTypeRefs.Add(runtimeTypeRef, typeRef);
            }
        }

        public void Register(RegisteredType registeredType)
        {
            if (registeredType is null)
            {
                throw new ArgumentNullException(nameof(registeredType));
            }

            bool addToTypes = !_typeRegister.ContainsValue(registeredType);

            foreach (ITypeReference typeReference in registeredType.References)
            {
                _typeRegister[typeReference] = registeredType;
            }

            if (addToTypes)
            {
                _types.Add(registeredType);
            }
        }

        public void Register(NameString typeName, RegisteredType registeredType)
        {
            if (registeredType is null)
            {
                throw new ArgumentNullException(nameof(registeredType));
            }

            typeName.EnsureNotEmpty(nameof(typeName));

            if(registeredType.IsExtension)
            {
                return;
            }

            if(!registeredType.IsNamedType && !registeredType.IsDirectiveType)
            {
                return;
            }

            if (TryGetTypeRef(typeName, out ITypeReference? typeRef) &&
                TryGetType(typeRef, out RegisteredType? type) &&
                !ReferenceEquals(type, registeredType))
            {
                throw TypeInitializer_DuplicateTypeName(registeredType.Type, type.Type);
            }

            _nameRefs[typeName] = registeredType.References[0];
        }

        public void RebuildIndexes()
        {
            _types.Clear();
            _types.AddRange(_typeRegister.Values.Distinct());
        }
    }
}
