﻿using Chloe.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Chloe.InternalExtensions;
using Chloe.Infrastructure;
using System.Collections.ObjectModel;

namespace Chloe.Entity
{
    internal class InternalEntityTypeBuilder<TEntity> : EntityTypeBuilder<TEntity>
    {
        public InternalEntityTypeBuilder()
        {
            this.ConfigureTableMapping();
            this.ConfigureColumnMapping();
            this.ConfigureNavigationProperty();
        }

        void ConfigureTableMapping()
        {
            var entityAttributes = this.EntityType.Type.GetCustomAttributes();
            foreach (Attribute entityAttribute in entityAttributes)
            {
                this.EntityType.Annotations.Add(entityAttribute);

                TableAttribute tableAttribute = entityAttribute as TableAttribute;
                if (tableAttribute != null)
                {
                    if (!string.IsNullOrEmpty(tableAttribute.Name))
                        this.MapTo(tableAttribute.Name);

                    this.HasSchema(tableAttribute.Schema);
                }
            }
        }
        void ConfigureColumnMapping()
        {
            var propertyInfos = this.EntityType.PrimitiveProperties.Select(a => a.Property).ToList();
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                IPrimitivePropertyBuilder propertyBuilder = this.Property(propertyInfo.Name);

                propertyBuilder.IsPrimaryKey(false);
                propertyBuilder.IsAutoIncrement(false);

                var propertyAttributes = propertyInfo.GetCustomAttributes();
                foreach (Attribute propertyAttribute in propertyAttributes)
                {
                    if (propertyAttribute is NotMappedAttribute)
                    {
                        this.Ignore(propertyInfo.Name);
                    }

                    if (propertyAttribute is NotNullAttribute)
                    {
                        propertyBuilder.IsNullable(false);
                    }

                    propertyBuilder.HasAnnotation(propertyAttribute);

                    if (propertyAttribute is ColumnAttribute)
                    {
                        ColumnAttribute columnAttribute = (ColumnAttribute)propertyAttribute;

                        if (!string.IsNullOrEmpty(columnAttribute.Name))
                            propertyBuilder.MapTo(columnAttribute.Name);

                        /* 为防止覆盖 IsPrimaryKey() 里的 DbType 设置，IsPrimaryKey() 方法调用在 HasDbType() 之后 */
                        propertyBuilder.HasDbType(columnAttribute.GetDbType());
                        propertyBuilder.IsPrimaryKey(columnAttribute.IsPrimaryKey);
                        propertyBuilder.IsRowVersion(columnAttribute.IsRowVersion);
                        propertyBuilder.HasSize(columnAttribute.GetSize());
                        propertyBuilder.HasScale(columnAttribute.GetScale());
                        propertyBuilder.HasPrecision(columnAttribute.GetPrecision());
                    }

                    if (propertyAttribute is AutoIncrementAttribute)
                    {
                        propertyBuilder.IsAutoIncrement(true);
                    }

                    SequenceAttribute sequenceAttribute = propertyAttribute as SequenceAttribute;
                    if (sequenceAttribute != null)
                    {
                        propertyBuilder.HasSequence(sequenceAttribute.Name, sequenceAttribute.Schema);
                    }
                }
            }

            List<PrimitiveProperty> primaryKeys = this.EntityType.PrimitiveProperties.Where(a => a.IsPrimaryKey).ToList();
            if (primaryKeys.Count == 0)
            {
                //如果没有定义任何主键，则从所有映射的属性中查找名为 id 的属性作为主键
                PrimitiveProperty idNameProperty = this.EntityType.PrimitiveProperties.Find(a => string.Equals(a.Property.Name, "id", StringComparison.OrdinalIgnoreCase) && !a.Property.IsDefined(typeof(ColumnAttribute)));

                if (idNameProperty != null)
                {
                    this.Property(idNameProperty.Property.Name).IsPrimaryKey();
                    primaryKeys.Add(idNameProperty);
                }
            }

            if (primaryKeys.Count == 1 && this.EntityType.PrimitiveProperties.Count(a => a.IsAutoIncrement) == 0)
            {
                /* 如果没有显示定义自增成员，并且主键只有 1 个，如果该主键满足一定条件，则默认其是自增列 */
                PrimitiveProperty primaryKey = primaryKeys[0];

                if (string.IsNullOrEmpty(primaryKey.SequenceName) && Utils.IsAutoIncrementType(primaryKey.Property.PropertyType) && !primaryKey.Property.IsDefined(typeof(NonAutoIncrementAttribute)))
                {
                    this.Property(primaryKey.Property.Name).IsAutoIncrement();
                }
            }
        }
        void ConfigureNavigationProperty()
        {
            PropertyInfo[] properties = this.EntityType.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(a => a.GetSetMethod() != null && a.GetGetMethod() != null).ToArray();

            foreach (PropertyInfo property in properties)
            {
                if (MappingTypeSystem.IsMappingType(property.PropertyType))
                    continue;

                if (this.IsSupportedCollectionType(property.PropertyType))
                {
                    this.EntityType.CollectionProperties.Add(new CollectionProperty(property));
                    continue;
                }

                ForeignKeyAttribute foreignKeyAttribute = property.GetCustomAttribute<ForeignKeyAttribute>();
                if (foreignKeyAttribute != null)
                {
                    ComplexProperty complexProperty = new ComplexProperty(property);
                    complexProperty.ForeignKey = foreignKeyAttribute.Name;
                    this.EntityType.ComplexProperties.Add(complexProperty);
                }
            }
        }

        bool IsSupportedCollectionType(Type type)
        {
            if (!type.IsGenericType)
                return false;

            type = type.GetGenericTypeDefinition();
            return type.IsAssignableFrom(typeof(List<>)) || type.IsAssignableFrom(typeof(Collection<>));
        }
    }
}
