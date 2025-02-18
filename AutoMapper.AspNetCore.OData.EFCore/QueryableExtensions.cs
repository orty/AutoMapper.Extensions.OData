﻿using AutoMapper.Extensions.ExpressionMapping;
using LogicBuilder.Expressions.Utils.Expansions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoMapper.AspNet.OData
{
    using System.Reflection;

    public static class QueryableExtensions
    {
        private static readonly MethodInfo SetParametersMethodInfo =
            Type.GetType("AutoMapper.QueryableExtensions.Impl.ParameterExpressionVisitor, AutoMapper").GetMethod("SetParameters", BindingFlags.Public | BindingFlags.Static);

        public static ICollection<TModel> Get<TModel, TData>(this IQueryable<TData> query, IMapper mapper, ODataQueryOptions<TModel> options, QuerySettings querySettings = null)
            where TModel : class
        {
            Expression<Func<TModel, bool>> filter = options.ToFilterExpression<TModel>(
                querySettings?.ODataSettings?.HandleNullPropagation ?? HandleNullPropagationOption.False,
                querySettings?.ODataSettings?.TimeZone);

            query.ApplyOptions(mapper, filter, options, querySettings);
            return query.Get
            (
                mapper,
                filter,
                options.GetQueryableExpression(querySettings?.ODataSettings),
                options.SelectExpand.GetIncludes().BuildIncludesExpressionCollection<TModel>()?.ToList()
            );
        }

        public static async Task<ICollection<TModel>> GetAsync<TModel, TData>(this IQueryable<TData> query, IMapper mapper, ODataQueryOptions<TModel> options, QuerySettings querySettings = null)
            where TModel : class
        {            
            Expression<Func<TModel, bool>> filter = options.ToFilterExpression<TModel>(
                querySettings?.ODataSettings?.HandleNullPropagation ?? HandleNullPropagationOption.False,
                querySettings?.ODataSettings?.TimeZone);
            await query.ApplyOptionsAsync(mapper, filter, options, querySettings);
            return await query.GetAsync
            (
                mapper,
                filter,
                options.GetQueryableExpression(querySettings?.ODataSettings),
                options.SelectExpand.GetIncludes().BuildIncludesExpressionCollection<TModel>()?.ToList(),
                querySettings?.AsyncSettings
            );
        }

        public static async Task<IQueryable<TModel>> GetQueryAsync<TModel, TData>(this IQueryable<TData> query, IMapper mapper, ODataQueryOptions<TModel> options, QuerySettings querySettings = null)
            where TModel : class
        {
            Expression<Func<TModel, bool>> filter = options.ToFilterExpression<TModel>(
                     querySettings?.ODataSettings?.HandleNullPropagation ?? HandleNullPropagationOption.False,
                     querySettings?.ODataSettings?.TimeZone);
                
            await query.ApplyOptionsAsync(mapper, filter, options, querySettings);
            return query.GetQueryable(mapper, options, querySettings, filter);
        }

        public static IQueryable<TModel> GetQuery<TModel, TData>(this IQueryable<TData> query, IMapper mapper, ODataQueryOptions<TModel> options, QuerySettings querySettings = null)
            where TModel : class
        {
            Expression<Func<TModel, bool>> filter = options.ToFilterExpression<TModel>(
                querySettings?.ODataSettings?.HandleNullPropagation ?? HandleNullPropagationOption.False,
                querySettings?.ODataSettings?.TimeZone);
            query.ApplyOptions(mapper, filter, options, querySettings);
            return query.GetQueryable(mapper, options, querySettings, filter);
        }

        public static ICollection<TModel> Get<TModel, TData>(this IQueryable<TData> query, IMapper mapper,
            Expression<Func<TModel, bool>> filter = null,
            Expression<Func<IQueryable<TModel>, IQueryable<TModel>>> queryFunc = null,
            ICollection<Expression<Func<IQueryable<TModel>, IIncludableQueryable<TModel, object>>>> includeProperties = null)
            => mapper.Map<IEnumerable<TData>, IEnumerable<TModel>>
            (
                query.GetDataQuery(mapper, filter, queryFunc, includeProperties).ToList()
            ).ToList();


        public static async Task<ICollection<TModel>> GetAsync<TModel, TData>(this IQueryable<TData> query, IMapper mapper,
            Expression<Func<TModel, bool>> filter = null,
            Expression<Func<IQueryable<TModel>, IQueryable<TModel>>> queryFunc = null,
            ICollection<Expression<Func<IQueryable<TModel>, IIncludableQueryable<TModel, object>>>> includeProperties = null,
            AsyncSettings asyncSettings = null)
            => mapper.Map<IEnumerable<TData>, IEnumerable<TModel>>
            (
                await query
                    .GetDataQuery(mapper, filter, queryFunc, includeProperties)
                    .ToListAsync
                    (
                        asyncSettings?.CancellationToken ?? default
                    )
            ).ToList();

        public static async Task ApplyOptionsAsync<TModel, TData>(this IQueryable<TData> query, IMapper mapper, Expression<Func<TModel, bool>> filter, ODataQueryOptions<TModel> options, QuerySettings querySettings)
        {
            ApplyOptions(options, querySettings);
            if (options.Count?.Value == true)
                options.AddCountOptionsResult(await query.QueryLongCountAsync(mapper, filter, querySettings?.AsyncSettings?.CancellationToken ?? default));
        }

        private static IQueryable<TData> GetDataQuery<TModel, TData>(this IQueryable<TData> query, IMapper mapper,
            Expression<Func<TModel, bool>> filter = null,
            Expression<Func<IQueryable<TModel>, IQueryable<TModel>>> queryFunc = null,
            ICollection<Expression<Func<IQueryable<TModel>, IIncludableQueryable<TModel, object>>>> includeProperties = null)
        {
            Expression<Func<TData, bool>> f = mapper.MapExpression<Expression<Func<TData, bool>>>(filter);
            Func<IQueryable<TData>, IQueryable<TData>> mappedQueryFunc = mapper.MapExpression<Expression<Func<IQueryable<TData>, IQueryable<TData>>>>(queryFunc)?.Compile();
            ICollection<Expression<Func<IQueryable<TData>, IIncludableQueryable<TData, object>>>> includes = mapper.MapIncludesList<Expression<Func<IQueryable<TData>, IIncludableQueryable<TData, object>>>>(includeProperties);

            if (filter != null)
                query = query.Where(f);

            if (includes != null)
                query = includes.Select(i => i.Compile()).Aggregate(query, (q, next) => q = next(q));

            return mappedQueryFunc != null ? mappedQueryFunc(query) : query;
        }

        private static IQueryable<TModel> GetQueryable<TModel, TData>(this IQueryable<TData> query,
            IMapper mapper,
            ODataQueryOptions<TModel> options,
            QuerySettings querySettings,
            Expression<Func<TModel, bool>> filter)
            where TModel : class
        {
            
            var expansions = options.SelectExpand.GetExpansions(typeof(TModel));

            return query.GetQuery
            (
                mapper,
                filter,
                options.GetQueryableExpression(querySettings?.ODataSettings),
                expansions
                    .Select(list => new List<Expansion>(list))
                    .BuildIncludes<TModel>(options.SelectExpand.GetSelects())
                    .ToList(),
                querySettings?.ProjectionSettings
            ).UpdateQueryableExpression(expansions, options.Context);
        }

        private static IQueryable<TModel> GetQuery<TModel, TData>(this IQueryable<TData> query,
            IMapper mapper,
            Expression<Func<TModel, bool>> filter = null,
            Expression<Func<IQueryable<TModel>, IQueryable<TModel>>> queryFunc = null,
            IEnumerable<Expression<Func<TModel, object>>> includeProperties = null,
            ProjectionSettings projectionSettings = null)
        {
            Expression<Func<TData, bool>> f = mapper.MapExpression<Expression<Func<TData, bool>>>(filter);
            Func<IQueryable<TData>, IQueryable<TData>> mappedQueryFunc = mapper.MapExpression<Expression<Func<IQueryable<TData>, IQueryable<TData>>>>(queryFunc)?.Compile();

            if (filter != null)
            {
                var parameterizedF = SetParametersMethodInfo.Invoke(
                                         null,
                                         new[] {projectionSettings?.Parameters, f}) as Expression<Func<TData, bool>>;
                query = query.Where(parameterizedF ?? f);
            }

            return mappedQueryFunc != null
                    ? mapper.ProjectTo(mappedQueryFunc(query), projectionSettings?.Parameters, GetIncludes())
                    : mapper.ProjectTo(query, projectionSettings?.Parameters, GetIncludes());

            Expression<Func<TModel, object>>[] GetIncludes() => includeProperties?.ToArray() ?? new Expression<Func<TModel, object>>[] { };
        }

        private static void ApplyOptions<TModel, TData>(this IQueryable<TData> query, IMapper mapper, Expression<Func<TModel, bool>> filter, ODataQueryOptions<TModel> options, QuerySettings querySettings)
        {
            ApplyOptions(options, querySettings);
            if (options.Count?.Value == true)
                options.AddCountOptionsResult(query.QueryLongCount(mapper, filter));
        }

        private static void ApplyOptions<TModel>(ODataQueryOptions<TModel> options, QuerySettings querySettings)
        {
            options.AddExpandOptionsResult();
            if (querySettings?.ODataSettings?.PageSize.HasValue == true)
                options.AddNextLinkOptionsResult(querySettings.ODataSettings.PageSize.Value);
        }

        private static async Task<long> QueryLongCountAsync<TModel, TData>(this IQueryable<TData> query,
            IMapper mapper,
            Expression<Func<TModel, bool>> modelFilter,
            CancellationToken cancellationToken)
            => modelFilter == null
                ? await query.LongCountAsync(cancellationToken)
                : await query.LongCountAsync
                (
                    mapper.MapExpression<Expression<Func<TData, bool>>>(modelFilter),
                    cancellationToken
                );

        private static long QueryLongCount<TModel, TData>(this IQueryable<TData> query,
            IMapper mapper,
            Expression<Func<TModel, bool>> modelFilter)
            => modelFilter == null
                ? query.LongCount()
                : query.LongCount
                (
                    mapper.MapExpression<Expression<Func<TData, bool>>>(modelFilter)
                );
    }
}
