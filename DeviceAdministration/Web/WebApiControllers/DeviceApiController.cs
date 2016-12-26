﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Models;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Models.Commands;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.BusinessLogic;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Models;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Web.DataTables;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Web.Security;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Repository;
using static Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Extensions.TwinCollectionExtension;

namespace Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Web.WebApiControllers
{
    [RoutePrefix("api/v1/devices")]
    public class DeviceApiController : WebApiControllerBase
    {
        private readonly IDeviceLogic _deviceLogic;
        private readonly IDeviceListFilterRepository _filterRepository;
        private readonly IIoTHubDeviceManager _deviceManager;

        public DeviceApiController(IDeviceLogic deviceLogic,IDeviceListFilterRepository filterRepository,IIoTHubDeviceManager deviceManager)
        {
            this._deviceLogic = deviceLogic;
            this._filterRepository = filterRepository;
            this._deviceManager = deviceManager;
        }

        // POST: api/v1/devices/sample/5
        [HttpPost]
        [Route("sample/{count}")]
        [WebApiRequirePermission(Permission.AddDevices)]
        public async Task<HttpResponseMessage> GenerateSampleDevicesAsync(int count)
        {
            ValidatePositiveValue("count", count);

            return await GetServiceResponseAsync(async () =>
            {
                await _deviceLogic.GenerateNDevices(count);
                return true;
            });
        }

        // GET: api/v1/devices/5
        [HttpGet]
        [Route("{id}")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> GetDeviceAsync(string id)
        {
            ValidateArgumentNotNullOrWhitespace("id", id);

            return await GetServiceResponseAsync<DeviceModel>(async () => (await _deviceLogic.GetDeviceAsync(id)));
        }

        // GET: api/v1/devices
        //
        // This endpoint is used for apps and other platforms to get a list of devices (whereas endpoint below is used by jQuery DataTables grid)
        // Filter arrays use the following syntax: ?test=1&test=2&test=3 for [FromUri] int[] test
        //
        // See, for example: http://stackoverflow.com/questions/9981330/how-to-pass-an-array-of-integers-to-a-asp-net-web-api-rest-service
        // Example: api/v1/devices?filterColumn=DeviceID&clauseType=StartsWithCaseSensitive&clauseValue=000&filterColumn=FirmwareVersion&clauseType=ContainsCaseInsensitive&clauseValue=564
        [HttpGet]
        [Route("")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> GetDevicesAsync(
            [FromUri]string search = null,
            [FromUri]string sortColumn = null,
            [FromUri]QuerySortOrder sortOrder = QuerySortOrder.Ascending,
            [FromUri]int skip = 0,
            [FromUri]int take = 50,
            [FromUri]string[] filterColumn = null,
            [FromUri]ClauseType[] clauseType = null,
            [FromUri]string[] clauseValue = null)
        {
            var clauses = new List<Clause>();
            if (filterColumn != null && clauseType != null && clauseValue != null)
            {
                // valid filters must send ALL three values--ignore unmatched extras
                int validFiltersCount = Math.Min(filterColumn.Length, Math.Min(clauseType.Length, clauseValue.Length));
                for (int i = 0; i < validFiltersCount; ++i)
                {
                    var clause = new Clause()
                    {
                        ColumnName = filterColumn[i],
                        ClauseType = clauseType[i],
                        ClauseValue = clauseValue[i]
                    };

                    clauses.Add(clause);
                }
            }

            var filter = new DeviceListFilter()
            {
                SearchQuery = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder,
                Skip = skip,
                Take = take,
                Clauses = clauses
            };

            return await GetServiceResponseAsync(async () => (await _deviceLogic.GetDevices(filter)).Results);
        }

        // POST: api/v1/devices/list
        // This endpoint is used by the jQuery DataTables grid to get data (and accepts an unusual data format based on that grid)
        [HttpPost]
        [Route("list")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> GetDevices([FromBody]JObject requestData)
        {
            return await GetServiceResponseAsync<DataTablesResponse<DeviceModel>>(async () =>
            {
                var dataTableRequest = requestData.ToObject<DataTablesRequest>();
                var sortColumnIndex = dataTableRequest.SortColumns[0].ColumnIndex;

                var listFilter = new DeviceListFilter()
                {
                    Id = dataTableRequest.FilterId,
                    Name = dataTableRequest.FilterName,

                    SortOrder = dataTableRequest.SortColumns[0].SortOrder,
                    SortColumn = dataTableRequest.Columns[sortColumnIndex].Data,

                    SearchQuery = dataTableRequest.Search.Value,

                    Clauses = dataTableRequest.Clauses,
                    AdvancedClause = dataTableRequest.AdvancedClause,
                    IsAdvanced = dataTableRequest.IsAdvanced,

                    Skip = dataTableRequest.Start,
                    Take = dataTableRequest.Length
                };

                var filterResult = await _deviceLogic.GetDevices(listFilter);

                var dataTablesResponse = new DataTablesResponse<DeviceModel>()
                {
                    Draw = dataTableRequest.Draw,
                    RecordsTotal = filterResult.TotalDeviceCount,
                    RecordsFiltered = filterResult.TotalFilteredCount,
                    Data = filterResult.Results.ToArray()
                };

                return dataTablesResponse;

            }, false);
        }

        // DELETE: api/v1/devices/5
        [HttpDelete]
        [Route("{id}")]
        [WebApiRequirePermission(Permission.RemoveDevices)]
        public async Task<HttpResponseMessage> RemoveDeviceAsync(string id)
        {
            ValidateArgumentNotNullOrWhitespace("id", id);

            return await GetServiceResponseAsync(async () =>
            {
                await _deviceLogic.RemoveDeviceAsync(id);
                return true;
            });
        }

        // POST: api/v1/devices
        [HttpPost]
        [Route("")]
        [WebApiRequirePermission(Permission.AddDevices)]
        public async Task<HttpResponseMessage> AddDeviceAsync(DeviceModel device)
        {
            ValidateArgumentNotNull("device", device);

            return await GetServiceResponseAsync<DeviceWithKeys>(async () =>
            {
                return await this._deviceLogic.AddDeviceAsync(device);
            });
        }

        [HttpPut]
        [Route("")]
        [WebApiRequirePermission(Permission.EditDeviceMetadata)]
        public async Task<HttpResponseMessage> UpdateDeviceAsync(DeviceModel device)
        {
            ValidateArgumentNotNull("device", device);
            return await GetServiceResponseAsync<bool>(async () =>
            {
                await _deviceLogic.UpdateDeviceAsync(device);
                return true;
            });
        }

        //GET: api/v1/devices/5/hub-keys
        [HttpGet]
        [Route("{id}/hub-keys")]
        [WebApiRequirePermission(Permission.ViewDeviceSecurityKeys)]
        public async Task<HttpResponseMessage> GetDeviceKeysAsync(string id)
        {
            ValidateArgumentNotNullOrWhitespace("id", id);

            return await GetServiceResponseAsync<SecurityKeys>(async () =>
            {
                return await _deviceLogic.GetIoTHubKeysAsync(id);
            });
        }

        //PUT: api/v1/devices/5/enabledstatus
        [HttpPut]
        [Route("{deviceId}/enabledstatus")]
        [WebApiRequirePermission(Permission.DisableEnableDevices)]
        public async Task<HttpResponseMessage> UpdateDeviceEnabledStatus(string deviceId, [FromBody]JObject request)
        {
            bool isEnabled;

            ValidateArgumentNotNullOrWhitespace("deviceId", deviceId);

            if (request == null)
                return GetNullRequestErrorResponse<bool>();

            try
            {
                var property = request.Property("isEnabled");

                if (property == null)
                {
                    return GetFormatErrorResponse<bool>("isEnabled", "bool");
                }

                isEnabled = request.Value<bool>("isEnabled");
            }
            catch (Exception)
            {
                return GetFormatErrorResponse<bool>("isEnabled", "bool");
            }

            return await GetServiceResponseAsync(async () =>
            {
                DeviceModel device = await _deviceLogic.UpdateDeviceEnabledStatusAsync(deviceId, isEnabled);
                return true;
            });
        }

        // POST: api/v1/devices/5/commands/{commandName}
        [HttpPost]
        [Route("{deviceId}/commands/{commandName}")]
        [WebApiRequirePermission(Permission.SendCommandToDevices)]
        public async Task<HttpResponseMessage> SendCommand(string deviceId, string commandName, [FromUri] DeliveryType deliveryType, [FromBody]dynamic parameters)
        {
            ValidateArgumentNotNullOrWhitespace("deviceId", deviceId);
            ValidateArgumentNotNullOrWhitespace("commandName", commandName);

            return await GetServiceResponseAsync(async () =>
            {
                await _deviceLogic.SendCommandAsync(deviceId, commandName, deliveryType, parameters);
                return true;
            });
        }

        // the following is somewhat dangerous if called accidentally--only include it in Debug mode
        // note that users will need to disable CSRF if calling from a web browser
#if DEBUG
        // DELETE: api/v1/all-devices
        [HttpDelete]
        [Route("~/api/v1/all-devices")]
        [WebApiRequirePermission(Permission.RemoveDevices)]
        public async Task<HttpResponseMessage> DeleteAllDevices()
        {
            return await GetServiceResponseAsync(async () =>
            {
                // note that you could also hardcode a filter to delete a subset of devices
                var filter = new DeviceListFilter()
                {
                    Skip = 0,
                    Take = 1000,
                    SortColumn = "twin.deviceId",
                };

                DeviceListFilterResult devices = await _deviceLogic.GetDevices(filter);

                foreach (var d in devices.Results)
                {
                    if (d.DeviceProperties != null && d.DeviceProperties.DeviceID != null)
                    {
                        string deviceId = d.DeviceProperties.DeviceID;

                        // do this in serial so as not to overload anything
                        Debug.Write("DELETING DEVICE: " + deviceId + "...");
                        await _deviceLogic.RemoveDeviceAsync(deviceId);
                        Debug.WriteLine("  (Deleted)");
                    }
                }
                return true;
            });
        }
#endif

        [HttpGet]
        [Route("")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> GetDevicesByFilterAsync([FromUri] string filterName, [FromUri]string jobId = null)
        {
            // todo: generate some value
            var result = new DeviceListFilterResult();
            result.TotalDeviceCount = 10;
            result.TotalFilteredCount = 1;

            var sampleTwin = new Twin()
            {
                DeviceId = "deviceID1",
                Properties = new TwinProperties()
                {
                    Desired = new TwinCollection() { },
                    Reported = new TwinCollection() { }
                },
                Tags = new TwinCollection()
            };

            result.Results.Add(new DeviceModel() { Twin = sampleTwin, IsSimulatedDevice = true });

            return await GetServiceResponseAsync<DeviceListFilterResult>(async () => (await Task.FromResult(result)));
        }

        [HttpGet]
        [Route("{deviceId}/methods")]
        [WebApiRequirePermission(Permission.ViewActions)]
        public async Task<HttpResponseMessage> GetMethodByDeviceIdAsync(string deviceId)
        {
            // TODO: get twin object and find reported.methods
            var methods = new List<string>();
            methods.Add("method1:int");
            methods.Add("method2(int,datetime):int");

            return await GetServiceResponseAsync<IEnumerable<string>>(async () => (await Task.FromResult(methods)));
        }

        [HttpPost]
        [Route("{deviceId}/methods/{methodName}")]
        [WebApiRequirePermission(Permission.SendCommandToDevices)]
        public async Task<HttpResponseMessage> InvokeMethod(string deviceId, string commandName, [FromBody]dynamic parameters)
        {
            return await GetServiceResponseAsync(async () =>
            {
                //await _deviceLogic.SendCommandAsync(deviceId, commandName, deliveryType, parameters);
                await Task.FromResult(true);
            });
        }

        [HttpPost]
        [Route("count/{filterId}")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> GetApplicableDeviceCountByMethod(string filterId,[FromBody] dynamic method )
        {
            string queryColumnName;
            string methodNamePostfix = "";

            // Deal multi-params method name postfix
            foreach (dynamic param in method.parameters)
            {
                methodNamePostfix += $"_{param.Type}";
            }
            
            // Build twin property name for supported method
            queryColumnName = $"reported.SupportedMethods.{method.methodName}{methodNamePostfix}";
            DeviceListFilter rawfilter = new DeviceListFilter();
            DeviceListFilter methodfilter = new DeviceListFilter();
            if (filterId == "*" || filterId == DeviceListFilterRepository.DefaultDeviceListFilter.Id)
            {

                rawfilter = DeviceListFilterRepository.DefaultDeviceListFilter;
                methodfilter = rawfilter.AddClause(new Clause() { ColumnName = queryColumnName, ClauseType = ClauseType.ISDEFINED, ClauseValue = "" });
            }
            else
            {
                rawfilter = await _filterRepository.GetFilterAsync(filterId);
                methodfilter = rawfilter.AddClause(new Clause() { ColumnName = queryColumnName, ClauseType = ClauseType.ISDEFINED, ClauseValue = "" });
            }

            var totalDeviceForRawFilter = await _deviceManager.GetDeviceCountAsync(rawfilter);
            var methodApplicableDeviceForFilter = await _deviceManager.GetDeviceCountAsync(methodfilter);

            return await GetServiceResponseAsync(async () =>
            {
               return await Task.FromResult(new { Total = totalDeviceForRawFilter, Applicable = methodApplicableDeviceForFilter });
            });
        }

        [HttpPost]
        [Route("count/{filterId}/save")]
        [WebApiRequirePermission(Permission.ViewDevices)]
        public async Task<HttpResponseMessage> SaveApplicableDeviceFilter(string filterId,bool isMatched, [FromBody] dynamic method)
        {
            string queryColumnName;
            string methodNamePostfix = "";

            // Deal multi-params method name postfix
            foreach (dynamic param in method.parameters)
            {
                methodNamePostfix += $"_{param.Type}";
            }

            // Build twin property name for supported method
            queryColumnName = $"reported.SupportedMethods.{method.methodName}{methodNamePostfix}";
            DeviceListFilter rawfilter = new DeviceListFilter();
            DeviceListFilter methodfilter = new DeviceListFilter();
            if (filterId == "*" || filterId == DeviceListFilterRepository.DefaultDeviceListFilter.Id)
            {

                rawfilter = DeviceListFilterRepository.DefaultDeviceListFilter;
                methodfilter = rawfilter.AddClause(new Clause() { ColumnName = queryColumnName, ClauseType = ClauseType.ISDEFINED, ClauseValue = "" });
            }
            else
            {
                rawfilter = await _filterRepository.GetFilterAsync(filterId);
                methodfilter = rawfilter.AddClause(new Clause() { ColumnName = queryColumnName, ClauseType = ClauseType.ISDEFINED, ClauseValue = "" });
            }

            if (isMatched)
            {
                methodfilter.AdvancedClause = methodfilter.GetSQLCondition();
                methodfilter.Name = $"{rawfilter.Name}_{method.methodName}_match";
            }
            else
            {
                methodfilter.AdvancedClause = methodfilter.GetSQLCondition() + "!= true";
                methodfilter.Name = $"{rawfilter.Name}_{method.methodName}_nomatch";
            }

            methodfilter.Id = Guid.NewGuid().ToString();
            methodfilter.IsAdvanced = true;
            methodfilter.IsTemporary = true;            
            var savedfilter = await _filterRepository.SaveFilterAsync(methodfilter, false);


            return await GetServiceResponseAsync(async () =>
            {
                return await Task.FromResult(new { filterId = savedfilter.Id});
            });
        }        

    }
}
