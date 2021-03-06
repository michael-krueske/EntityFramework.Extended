﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Mapping;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;

namespace EntityFramework.Mapping
{
    /// <summary>
    /// Use <see cref="MetadataWorkspace"/> to resolve mapping information.
    /// </summary>
    public class MetadataMappingProvider : IMappingProvider
    {
        /// <summary>
        /// Gets the <see cref="EntityMap" /> for the specified <typeparamref name="TEntity" />.
        /// </summary>
        /// <typeparam name="TEntity">The type of the entity.</typeparam>
        /// <param name="query">The query to use to help load the mapping data.</param>
        /// <returns>
        /// An <see cref="EntityMap" /> with the mapping data.
        /// </returns>
        public EntityMap GetEntityMap<TEntity>(ObjectQuery query)
        {
            var context = query.Context;
            var type = typeof(TEntity);

            return GetEntityMap(type, context);
        }

        public EntityMap GetEntityMap(Type type, DbContext dbContext)
        {
            var objectContextAdapter = dbContext as IObjectContextAdapter;
            var objectContext = objectContextAdapter.ObjectContext;
            return GetEntityMap(type, objectContext);
        }

        public EntityMap GetEntityMap(Type type, ObjectContext objectContext)
        {
            var entityMap = new EntityMap(type);
            var metadata = objectContext.MetadataWorkspace;

            // Get the part of the model that contains info about the actual CLR types
            var objectItemCollection = ((ObjectItemCollection)metadata.GetItemCollection(DataSpace.OSpace));

            // Get the entity type from the model that maps to the CLR type
            var entityType = metadata
                    .GetItems<EntityType>(DataSpace.OSpace)
                    .Single(e => objectItemCollection.GetClrType(e) == type);

            // Get the entity set that uses this entity type
            var entitySet = metadata
                .GetItems<EntityContainer>(DataSpace.CSpace)
                .Single()
                .EntitySets
                .Single(s => s.ElementType.Name == entityType.Name);

            // Find the mapping between conceptual and storage model for this entity set
            var mapping = metadata.GetItems<EntityContainerMapping>(DataSpace.CSSpace)
                    .Single()
                    .EntitySetMappings
                    .Single(s => s.EntitySet == entitySet);

            // Find the storage entity set (table) that the entity is mapped
            var mappingFragment =
                (mapping.EntityTypeMappings.SingleOrDefault(a => a.IsHierarchyMapping) ?? mapping.EntityTypeMappings.Single())
                    .Fragments.Single();

            entityMap.ModelType = entityType;
            entityMap.ModelSet = entitySet;
            entityMap.StoreSet = mappingFragment.StoreEntitySet;
            entityMap.StoreType = mappingFragment.StoreEntitySet.ElementType;

            // set table
            SetTableName(entityMap);

            // set properties
            SetProperties(entityMap, mappingFragment);

            // set keys
            SetKeys(entityMap);

            return entityMap;
        }

        private static void SetKeys(EntityMap entityMap)
        {
            var modelType = entityMap.ModelType;
            foreach (var edmMember in modelType.KeyMembers)
            {
                var property = entityMap.PropertyMaps.FirstOrDefault(p => p.PropertyName == edmMember.Name);
                if (property == null)
                    throw new InvalidOperationException(string.Format("There is no mapping for key member '{0}'.", edmMember));

                var propertyMap = property as PropertyMap;
                if (propertyMap == null)
                    throw new InvalidOperationException(string.Format("KeyMember {1} of entity {0} cannot be complex.",
                        entityMap.TableName, edmMember.Name));

                entityMap.KeyMaps.Add(propertyMap);
            }
        }

        private static void SetProperties(EntityMap entityMap, MappingFragment mappingFragment)
        {
            foreach (var propertyMapping in mappingFragment.PropertyMappings)
            {
                var map = CreatePropertyMap(propertyMapping);
                entityMap.PropertyMaps.Add(map);
            }
        }

        private static IPropertyMapElement CreatePropertyMap(PropertyMapping propertyMapping)
        {
            var scalarPropertyMapping = propertyMapping as ScalarPropertyMapping;
            if (scalarPropertyMapping != null)
            {
                return new PropertyMap(propertyMapping.Property.Name, scalarPropertyMapping.Column.Name);
            }

            var complexPropertyMapping = propertyMapping as ComplexPropertyMapping;
            if (complexPropertyMapping != null)
            {
                var typeElements = 
                    from typeMapping in complexPropertyMapping.TypeMappings 
                    from property in typeMapping.PropertyMappings 
                    select CreatePropertyMap(property);

                return new ComplexPropertyMap(propertyMapping.Property.Name, typeElements.ToList());
            }

            throw new InvalidOperationException("Invalid or unknown propertyMapping type: " + propertyMapping.GetType());
        }

        private static void SetTableName(EntityMap entityMap)
        {
            var builder = new StringBuilder(50);

            EntitySet storeSet = entityMap.StoreSet;

            string table = null;
            string schema = null;

            MetadataProperty tableProperty;
            MetadataProperty schemaProperty;

            storeSet.MetadataProperties.TryGetValue("Table", true, out tableProperty);
            if (tableProperty == null || tableProperty.Value == null)
                storeSet.MetadataProperties.TryGetValue("http://schemas.microsoft.com/ado/2007/12/edm/EntityStoreSchemaGenerator:Table", true, out tableProperty);

            if (tableProperty != null)
                table = tableProperty.Value as string;

            // Table will be null if its the same as Name
            if (table == null)
                table = storeSet.Name;

            storeSet.MetadataProperties.TryGetValue("Schema", true, out schemaProperty);
            if (schemaProperty == null || schemaProperty.Value == null)
                storeSet.MetadataProperties.TryGetValue("http://schemas.microsoft.com/ado/2007/12/edm/EntityStoreSchemaGenerator:Schema", true, out schemaProperty);

            if (schemaProperty != null)
                schema = schemaProperty.Value as string;

            if (!string.IsNullOrWhiteSpace(schema))
            {
                builder.Append(QuoteIdentifier(schema));
                builder.Append(".");
            }

            builder.Append(QuoteIdentifier(table));

            entityMap.TableName = builder.ToString();
        }

        private static string QuoteIdentifier(string name)
        {
            return ("[" + name.Replace("]", "]]") + "]");
        }

    }
}
