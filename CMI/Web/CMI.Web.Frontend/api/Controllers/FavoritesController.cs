﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;
using CMI.Access.Sql.Viaduc;
using CMI.Contract.Common;
using CMI.Web.Common.api;
using CMI.Web.Common.api.Attributes;
using CMI.Web.Common.Helpers;
using CMI.Web.Frontend.api.Elastic;
using CMI.Web.Frontend.api.Interfaces;
using Newtonsoft.Json.Linq;
using WebGrease.Css.Extensions;

namespace CMI.Web.Frontend.api.Controllers
{
    [Authorize]
    [NoCache]
    public class FavoritesController : ApiFrontendControllerBase
    {
        private readonly IElasticService elasticService;
        private readonly FavoriteDataAccess sqlDataAccess = new FavoriteDataAccess(WebHelper.Settings["sqlConnectionString"]);

        public FavoritesController(IElasticService elasticService)
        {
            this.elasticService = elasticService;
        }

        [HttpGet]
        public FavoriteList[] GetAllLists()
        {
            return sqlDataAccess.GetAllLists(ControllerHelper.GetCurrentUserId())
                .OrderBy(l => l.Name)
                .ToArray();
        }


        [HttpGet]
        public int GetFavoriteCount()
        {
            return sqlDataAccess.GetAllLists(ControllerHelper.GetCurrentUserId())
                .Sum(l => l.NumberOfItems);
        }

        [HttpGet]
        public JArray GetAllListsForUrl(string url)
        {
            var jls = new JArray();
            var ids = GetListContainingFavorite(url);

            sqlDataAccess.GetAllLists(ControllerHelper.GetCurrentUserId())
                .OrderBy(l => l.Name)
                .ForEach(l =>
                {
                    var jl = JObject.FromObject(l);
                    jl.Add("included", ids.Contains(l.Id));
                    jls.Add(jl);
                });

            return jls;
        }

        [HttpGet]
        public int[] GetListContainingFavorite(string url)
        {
            return sqlDataAccess.GetListContainingFavorite(ControllerHelper.GetCurrentUserId(), url).ToArray();
        }

        [HttpGet]
        public JArray GetAllListsForVe(string veId)
        {
            var jls = new JArray();
            var ids = GetListsContainingVe(veId);
            sqlDataAccess.GetAllLists(ControllerHelper.GetCurrentUserId())
                .OrderBy(l => l.Name)
                .ForEach(l =>
                {
                    var jl = JObject.FromObject(l);
                    jl.Add("included", ids.Contains(l.Id));
                    jls.Add(jl);
                });

            return jls;
        }

        [HttpGet]
        public FavoriteList GetList(int listId)
        {
            var list = sqlDataAccess.GetList(ControllerHelper.GetCurrentUserId(), listId);
            list.Items = GetFavoritesContainedOnList(listId);
            return list;
        }

        [HttpPost]
        public IHttpActionResult AddList(string listName)
        {
            if (string.IsNullOrWhiteSpace(listName))
            {
                return BadRequest($"missing parameter {nameof(listName)} ");
            }

            return Content(HttpStatusCode.Created, sqlDataAccess.AddList(ControllerHelper.GetCurrentUserId(), listName, null));
        }

        [HttpPost]
        public void RemoveList(int listId)
        {
            sqlDataAccess.RemoveList(ControllerHelper.GetCurrentUserId(), listId);
        }

        [HttpPost]
        public void RenameList(int listId, string newName)
        {
            sqlDataAccess.RenameList(ControllerHelper.GetCurrentUserId(), listId, newName);
        }

        [HttpPost]
        public IHttpActionResult AddSearchFavorite([FromUri] int listId, [FromBody] SearchFavorite favorite)
        {
            if (favorite == null)
            {
                return BadRequest($"missing parameter {nameof(favorite)}");
            }

            var favourite = sqlDataAccess.AddFavorite(ControllerHelper.GetCurrentUserId(), listId, favorite);
            return Content(HttpStatusCode.Created, favourite);
        }

        [HttpPost]
        public IHttpActionResult AddVeFavorite([FromUri] int listId, [FromBody] VeFavorite favorite)
        {
            if (favorite == null)
            {
                return BadRequest($"missing parameter {nameof(favorite)}");
            }

            var favourite = sqlDataAccess.AddFavorite(ControllerHelper.GetCurrentUserId(), listId, favorite);
            return Content(HttpStatusCode.Created, favourite);
        }

        [HttpPost]
        public void RemoveFavorite(int id, int listId)
        {
            sqlDataAccess.RemoveFavorite(ControllerHelper.GetCurrentUserId(), listId, id);
        }

        [HttpGet]
        public int[] GetListsContainingVe(string veId)
        {
            return sqlDataAccess.GetListsContainingVe(ControllerHelper.GetCurrentUserId(), veId).ToArray();
        }

        [HttpGet]
        public IEnumerable<IFavorite> GetFavoritesContainedOnList(int listId)
        {
            var uid = ControllerHelper.GetCurrentUserId();
            var favorites = sqlDataAccess.GetFavoritesContainedOnList(uid, listId).ToList();

            var veIds = favorites.OfType<VeFavorite>().Select(f => f.VeId).ToList();
            var access = GetUserAccess(WebHelper.GetClientLanguage(Request));

            var found = elasticService
                .QueryForIds<ElasticArchiveRecord>(veIds, access, new Paging {Take = ElasticService.ELASTIC_SEARCH_HIT_LIMIT, Skip = 0}).Entries;

            foreach (var f in favorites)
            {
                if (f is VeFavorite veFavorite)
                {
                    var elasticHit = found.FirstOrDefault(e => e?.Data?.ArchiveRecordId == veFavorite.VeId.ToString());
                    if (elasticHit == null)
                    {
                        continue;
                    }

                    veFavorite.Title = elasticHit.Data?.Title;
                    veFavorite.Level = elasticHit.Data?.Level;
                    veFavorite.CreationPeriod = elasticHit.Data?.CreationPeriod?.Text;
                    veFavorite.ReferenceCode = elasticHit.Data?.ReferenceCode;
                    veFavorite.CanBeOrdered = elasticHit.Data?.CanBeOrdered ?? false;
                    veFavorite.CanBeDownloaded = !string.IsNullOrWhiteSpace(elasticHit.Data?.PrimaryDataLink) &&
                                                 access.HasAnyTokenFor(elasticHit.Data.PrimaryDataDownloadAccessTokens);
                    yield return veFavorite;
                }
                else
                {
                    yield return f;
                }
            }
        }

        [HttpGet]
        public PendingMigrationCheckResult CurrentUserHasPendingMigrations()
        {
            var user = ControllerHelper.UserDataAccess.GetUser(ControllerHelper.GetCurrentUserId());
            if (user.Access.RolePublicClient == AccessRoles.RoleOe3
                || user.Access.RolePublicClient == AccessRoles.RoleBVW
                || user.Access.RolePublicClient == AccessRoles.RoleAS
                || user.Access.RolePublicClient == AccessRoles.RoleBAR)
            {
                return sqlDataAccess.HasPendingMigrations(user.EmailAddress);
            }

            return new PendingMigrationCheckResult();
        }

        [HttpPost]
        public void MigrateFavorites(string source)
        {
            if (source.Equals("local", StringComparison.InvariantCultureIgnoreCase) ||
                source.Equals("public", StringComparison.InvariantCultureIgnoreCase))
            {
                var user = ControllerHelper.UserDataAccess.GetUser(ControllerHelper.GetCurrentUserId());
                if (sqlDataAccess.MigrateFavorites(user.Id, user.EmailAddress, source))
                {
                    return;
                }

                throw new InvalidOperationException("Unexpected error during migration. See server logs for details.");
            }
        }
    }
}