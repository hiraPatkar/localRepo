using Asset_Management_UI.Areas.CommonOperations.Controllers;
using Asset_Management_UI.Areas.Report;
using Asset_Management_UI.Helper;
using Asset_Management_UI.Models;
using AssetManagementUI.Helper;
using AssetManagementUI.Model.Models;
using AssetManagementUI.Model.Models.ViewModels;
using AssetManagementUI.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;


namespace Asset_Management_UI.Areas.Asset.Controllers
{
    public class AddOrModifyProcedureController : Controller
    {
        private readonly ILogger<AddOrModifyProcedureController> _logger;
        private readonly ILogger<CommonOperationsController> _loggerForComOp;
        AssetManagementAPI _api;
        private readonly IMemoryCache _memoryCache;
        private readonly IMemoryCache _memoryCacheForUser;
        private readonly IHostingEnvironment? _hostingEnvironment;
        public string mainTab = "";
        public string subTab = "";
        private readonly IConfiguration? _configuration;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public AddOrModifyProcedureController(ILogger<AddOrModifyProcedureController> logger, IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache, IMemoryCache memoryCacheForUser, IHostingEnvironment? hostingEnvironment, IConfiguration configuration, ILogger<CommonOperationsController> loggerForComOp)
        {
            _logger = logger;
            _memoryCache = memoryCache;
            _memoryCacheForUser = memoryCacheForUser;
            _hostingEnvironment = hostingEnvironment;
            _configuration = configuration;
            _loggerForComOp = loggerForComOp;
            _api = new AssetManagementAPI(configuration);
            _httpContextAccessor = httpContextAccessor;
        }
        public IActionResult AddOrModifyProcedure()
        {
            return View();
        }



        //Gets table data from AddOrModify procedure page


        [HttpGet]
        public async Task<IActionResult> GetProcedureList()
        {
            try
            {
                long session_user = long.Parse(HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value);
                string session_user_name = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "login_name")?.Value;
                long session_user_location = long.Parse(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "locationId")?.Value);
                string session_user_empCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "empCode")?.Value;

                CommonOperationsController cont = new CommonOperationsController(_loggerForComOp, _httpContextAccessor, _memoryCache, _memoryCacheForUser, _configuration);

                HttpClient client = await cont.GetHttpClient();
               

                HttpResponseMessage res = await client.GetAsync(_configuration.GetSection("AM_API_URL").Value + "/Asset/AddOrModifyProcedure/GetProcedureList?session_user_location=" + session_user_location);
                if (res.IsSuccessStatusCode)
                {
                    var result = res.Content.ReadAsStringAsync().Result;
                    var resultSet = JsonConvert.DeserializeObject<AddOrModifyProcedureVM>(result);
                    resultSet.session_user_id = session_user;
                    resultSet.session_user_name = session_user_name;
                    resultSet.session_user_location = session_user_location;
                    return View("~/Views/Asset/AddOrModifyProcedure.cshtml", resultSet);
                }
                return NotFound();
            }
            catch (Exception e)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(e, $"Path: {controllerName + "/" + actionName}\n" + e.Message);
                return StatusCode(500, e);
            }
        }

        //gets dropdown values from asset type from create new procedure page

        [ValidateAntiForgeryToken]
		[HttpGet]
		public async Task<IActionResult> GetAssetTypeDropdown()
		{
			try
			{
                CommonOperationsController cont = new CommonOperationsController(_loggerForComOp, _httpContextAccessor, _memoryCache, _memoryCacheForUser, _configuration);

                HttpClient client = await cont.GetHttpClient();


                HttpResponseMessage res = await client.GetAsync(_configuration.GetSection("AM_API_URL").Value + "/Asset/AddOrModifyProcedure/GetAssetTypeDropdown");
				if (res.IsSuccessStatusCode)
				{
					var result = res.Content.ReadAsStringAsync().Result;
					var resultSet = JsonConvert.DeserializeObject<List<AssetType>>(result);
	
					return Json(new { resultSet}) ;
				}
				return NotFound();
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
				_logger.LogError(e, $"Path: {controllerName + "/" + actionName}\n" + e.Message);
				return StatusCode(500, e);
			}
		}

        [Authorize(Roles = "Add / Modify Procedure")]

        [HttpPost]

        public async Task<IActionResult> AddModifyDeleteAssetMaintenanceProcedure([FromForm] AddOrModifyProcedureVM addModifyProcedureModalData)
        {
            try
            {
                long session_user = long.Parse(HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value);
                string session_user_name = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "login_name")?.Value;
                long session_user_location = long.Parse(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "locationId")?.Value);

                addModifyProcedureModalData.session_user_location = session_user_location;
                addModifyProcedureModalData.session_user_id = session_user;
                if (addModifyProcedureModalData.procedureModalData.userInfo != null)
                {
                    addModifyProcedureModalData.procedureModalData.userInfo.SessionUserId = session_user;
                    addModifyProcedureModalData.procedureModalData.userInfo.LoginName = session_user_name;
                }

                addModifyProcedureModalData.procedureModalData.UserId = session_user.ToString();

                //CommonOperationsController cont = new CommonOperationsController(_loggerForComOp, _memoryCache, _memoryCacheForUser, _configuration);
                //var isUserAuthenticated = await cont.AuthenticateUser(addModifyProcedureModalData.procedureModalData.userInfo) as JsonResult;
                //var flag = (isUserAuthenticated != null) ? isUserAuthenticated.Value as dynamic : null;

                var jsonstring = JsonContent.Create(addModifyProcedureModalData.procedureModalData);
                //long session_user = long.Parse(HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value);
                addModifyProcedureModalData.procedureModalData.ApiVersion = "1.1";
                addModifyProcedureModalData.procedureModalData.UserId = session_user.ToString();

                if (addModifyProcedureModalData.procedureModalData.MaintenanceProcedureId != null && addModifyProcedureModalData.procedureModalData.MaintenanceProcedureId != "")
                {

                    if (addModifyProcedureModalData.procedureModalData.Operation == "delete")
                    {
                        jsonstring = JsonContent.Create(addModifyProcedureModalData);
                        CommonOperationsController cont1 = new CommonOperationsController(_loggerForComOp, _httpContextAccessor, _memoryCache, _memoryCacheForUser, _configuration);

                        HttpClient client1 = await cont1.GetHttpClient();


                        HttpResponseMessage response = await client1.PostAsync(_configuration.GetSection("AM_API_URL").Value + "/AssetProcedure/MaintenanceProcedure/DeleteAssetMaintenanceProcedure", jsonstring);
                        if (response.IsSuccessStatusCode)
                        {
                            var result = response.Content.ReadAsStringAsync().Result;
                            var resultSet = JsonConvert.DeserializeObject<ApiMessageViewModel>(result);
                            if (resultSet.apiStatusMessage == "Asset_Maintenance_Procedure_Deleted_Successfully")
                            {
                                return RedirectToAction("GetProcedureList", "AddOrModifyProcedure", new
                                {
                                    area = "Asset",
                                    message = resultSet.apiStatusMessage
                                });
                            }
                        }
                        //return RedirectToAction("AddAssetInfo", "Asset", new { area = "Asset" });
                        return RedirectToAction("GetProcedureList", "AddOrModifyProcedure", new
                        {
                            area = "Asset",
                            //message = resultSet.apiStatusMessage
                        });
                    }
                }

                if (addModifyProcedureModalData.procedureModalData.MaintenanceProcedureId != null && addModifyProcedureModalData.procedureModalData.MaintenanceProcedureId != "")
                {
                    addModifyProcedureModalData.procedureModalData.Operation = "U";
                    addModifyProcedureModalData.procedureModalData.RequestType = "Update Asset Maintenance Procedure";
                }
                else
                {
                    addModifyProcedureModalData.procedureModalData.Operation = "I";
                    addModifyProcedureModalData.procedureModalData.RequestType = "Add Asset Maintenance Procedure";
                }

                if (addModifyProcedureModalData.procedureModalData.procedureStepList != "")
                {
                    addModifyProcedureModalData.procedureModalData.procedureStepTextList = JsonConvert.DeserializeObject<List<AddProcedureViewModel>>(addModifyProcedureModalData.procedureModalData.procedureStepList);
                }

                var jsonstring1 = JsonContent.Create(addModifyProcedureModalData);
                CommonOperationsController cont = new CommonOperationsController(_loggerForComOp, _httpContextAccessor, _memoryCache, _memoryCacheForUser, _configuration);

                HttpClient client = await cont.GetHttpClient();


                HttpResponseMessage res = await client.PostAsync(_configuration.GetSection("AM_API_URL").Value + "/AssetProcedure/MaintenanceProcedure/AddModifyDeleteAssetMaintenanceProcedure", jsonstring1);

                if (res.IsSuccessStatusCode)
                {
                    var result = res.Content.ReadAsStringAsync().Result;
                    var resultSet = JsonConvert.DeserializeObject<ApiMessageViewModel>(result);
                    if (resultSet.apiStatusMessage == "At_Least_One_Procedure_Must_Be_Active")
                    {

							return RedirectToAction("GetProcedureList", "AddOrModifyProcedure", new
							{
								area = "Asset",
								message = resultSet.apiStatusMessage
							});
						}
						else if (resultSet.apiStatusMessage == "New asset maintenance procedure added")
						{
							if (resultSet != null && resultSet.statusCodeResult == "200")
							{
								TempData["statusMsg"] = resultSet.apiStatusMessage;
								TempData["statusCode"] = "1";
							}
							else
							{
								TempData["statusMsg"] = "Transaction Failed!";
								TempData["statusCode"] = "0";
							}
						}
						else if (resultSet.apiStatusMessage == "Procedure code already exists")
						{						
								TempData["statusMsg"] = resultSet.apiStatusMessage;
								TempData["statusCode"] = "6";
						}
						else if (resultSet.apiStatusMessage == "Asset maintenance procedure updated successfully")
						{
							if (resultSet != null && resultSet.statusCodeResult == "200")
							{
								TempData["statusMsg"] = resultSet.apiStatusMessage;
								TempData["statusCode"] = "1";
							}
							else
							{
								TempData["statusMsg"] = "Transaction Failed!";
								TempData["statusCode"] = "0";
							}
						}
						else
						{
							TempData["statusMsg"] = "Transaction Failed!";
							TempData["statusCode"] = "0";
						}

                    return RedirectToAction("GetProcedureList", "AddOrModifyProcedure", new
                    {
                        area = "Asset",
                        message = resultSet.apiStatusMessage
                    });
                }
                else
                {
                    TempData["statusMsg"] = "Transaction Failed!";
                    TempData["statusCode"] = "0";
                }


                //return RedirectToAction("AddAssetInfo", "Asset", new { area = "Asset" });
                return RedirectToAction("GetProcedureList", "AddOrModifyProcedure", new
                {
                    area = "Asset",
                    //message = resultSet.apiStatusMessage
                });
                //return Ok();
            }
            catch (Exception e)
            {
                string actionName = ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(e, $"Path: {controllerName + "/" + actionName}\n" + e.Message);
                // return 0;
                return StatusCode(500, e);
            }
        }
    }
}
