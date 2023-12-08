
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostSalesAPI.Data;
using PostSalesAPI.Models;
using Repository.IRepository;
using SharedLibrary;
using SharedLibrary.Models;
using SharedLibrary.Models.Dto;
using SharedLibrary.ViewModels;
using System;
using System.Linq;
using System.Reflection.Metadata;
using TimeZoneConverter;

namespace PostSalesAPI.Controllers
{
	[ApiController]
	[Route("[controller]/[action]")]
	public class PostSalesController : Controller
	{
		private IUnitOfWork _unitOfWork;
		private readonly ILogger _logger;
		private readonly IConfiguration? _configuration;
		private readonly AppDbContext _dbContext;
		private readonly string containerName;
		public PostSalesController(IUnitOfWork unitOfWork, ILogger<PostSalesController> logger, IConfiguration configuration, AppDbContext dbContext)
		{
			_unitOfWork = unitOfWork;
			_dbContext = dbContext;
			_logger = logger;
			_configuration = configuration;
			containerName = _configuration["AzureContainerDetails:containerName"];
		}


		[HttpGet]
		[Route("/")]
		public string Index()
		{
			return "API Working!!";
		}

		[HttpGet]
		[Route("/PostSales/GetAllLeadsDropdownData")]
		public ResponseDto GetAllLeadsDropdownData()
		{
			try
			{
				_logger.LogInformation("GetChartData endpoint executing");
				var codeTypeArray = new string[] { CodeTypes.Reminder_Type, CodeTypes.Call_Status, CodeTypes.Lead_Status, CodeTypes.State, CodeTypes.Stale, CodeTypes.Lead_Type, CodeTypes.City, CodeTypes.Category, CodeTypes.Carrier, CodeTypes.Campaign, CodeTypes.Type_Of_Order_List };
				var codesList = _unitOfWork.Codes.GetAll(c => codeTypeArray.Contains(c.CodeType) && c.Status == Status.Active).ToList();
				var leadStatusList = CodesList(CodeTypes.Lead_Status, codesList);
				var callStatusList = CodesList(CodeTypes.Call_Status, codesList)
									 .Where(code => code.CodeValue != "SALE" &&
											code.CodeValue != "CS").ToList();

				var reminderTypeList = CodesList(CodeTypes.Reminder_Type, codesList);


				var orderCancelledReasonList = (from c in _unitOfWork.Codes.GetAll(c => c.Status == Status.Active && c.CodeType == LeadStatus.Order_Cancelled_Reasons).ToList()
												select new CodeDto()
												{
													CodeId = c.CodeId,
													CodeValue = c.CodeValue,
													CodeDescription = c.CodeDescription,
													CodeComments = c.CodeComments,
												}).ToList();
				//code values for Post Sales
				var codeValuesForPostSales = new List<string>() { LeadStatus.Installed, LeadStatus.Scheduled ,
																  LeadStatus.Order_Cancelled,LeadStatus.PendSchedule,
																  LeadStatus.Construction, LeadStatus.WorkInProgress,
																  LeadStatus.Order
																};

				var postSalesStatusList = (from c in _unitOfWork.Codes.GetAll(c => c.Status == Status.Active
														&& codeValuesForPostSales.Contains(c.CodeValue)
														&& c.CodeType == "LEAD_STATUS").ToList()
										   select new CodeDto()
										   {
											   CodeId = c.CodeId,
											   CodeValue = c.CodeValue,
											   CodeDescription = c.CodeDescription,
											   CodeComments = c.CodeComments,
										   }).ToList();

				//dropdown values for type of order
				var typeOfOrderList = CodesList(CodeTypes.Type_Of_Order_List, codesList);


				return new ResponseDto()
				{
					IsSuccess = true,
					Result = new Dictionary<string, List<CodeDto>>
					{
						{CodeTypes.Lead_Status,leadStatusList},
						{CodeTypes.Call_Status,callStatusList},
						{CodeTypes.Reminder_Type,reminderTypeList },
						{CodeTypes.Post_Sales_Status,postSalesStatusList },
						{CodeTypes.Order_Cancelled_Reasons,orderCancelledReasonList },
						{CodeTypes.Type_Of_Order_List,typeOfOrderList }
					},
				};
			}
			catch (Exception e)
			{
				_logger.LogError(string.Format("{0}:{1}", "GetChartData", e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message }
				};
			}
		}

		[HttpGet]
		[Route("/PostSales/GetAllLeadsOfStatusSale")]
		public ResponseDto GetAllLeadsOfStatusSale()
		{
			try
			{
				var sessionUser = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimsKey.UserId)?.Value);
				if (sessionUser == 0)
					throw new InvalidDataException(ErrorMessages.Session_User_Not_Available);

				var codeValues = new List<string>() {   LeadStatus.Sale,
														LeadStatus.Installed, LeadStatus.Scheduled ,
														LeadStatus.Order_Cancelled,LeadStatus.PendSchedule,
														LeadStatus.Construction, LeadStatus.WorkInProgress,
														LeadStatus.Order
													};

				IQueryable<Leads> leadsList = null;

				leadsList = _dbContext.Leads.Where(s => codeValues.Contains(s.Status)).AsQueryable();

				IQueryable<Codes> codes = _dbContext.Codes.Where(s => codeValues.Contains(s.CodeValue) && s.Status == Status.Active && s.CodeType == "LEAD_STATUS").AsQueryable();

				IQueryable<Codes> leadTypeCodes = _dbContext.Codes.Where(s => s.CodeType == CodeTypes.Lead_Type).AsQueryable();

				IQueryable<User> users = _dbContext.User.Where(u => leadsList.ToList().Select(l => l.UserId).Contains(u.UserId)).AsQueryable();

				IQueryable<Call> calls = _dbContext.Call.Where(c => leadsList.ToList().Select(l => l.LeadsId.ToString()).Contains(c.LeadId.ToString())).AsQueryable();

				IQueryable<Order> orders = _dbContext.Orders.Where(c => leadsList.ToList().Select(l => l.LeadsId.ToString()).Contains(c.LeadId.ToString())).AsQueryable();

				Dictionary<long, string?> callbackTime = new Dictionary<long, string>();

				List<LeadsVM> resultSet = GetLeadsData(leadsList, codes, leadTypeCodes, users, calls, orders);

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = resultSet
				};
			}
			catch (Exception e)
			{
				_logger.LogError(string.Format("{0}:{1}", "GetAllLeadsOfStatusSale", e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message }
				};
			}
		}

		[NonAction]
		private List<LeadsVM> GetLeadsData(IQueryable<Leads> leadsList, IQueryable<Codes> leadStatusCodes,
											IQueryable<Codes> leadTypeCodes, IQueryable<Models.User> users,
											IQueryable<Call> callsList, IQueryable<Order> orders)
		{
			return (from l in leadsList
					join c in leadStatusCodes on l.Status equals c.CodeValue
					join u in users on l.UserId equals u.UserId
					join ltc in leadTypeCodes on l.LeadType.ToString().ToLower() equals ltc.CodeValue into ltcGroup
					from ltc in ltcGroup.DefaultIfEmpty()
					join ord in orders on l.LeadsId equals ord.LeadId into lOrderGroup
					from ord in lOrderGroup.DefaultIfEmpty()
					select new LeadsVM()
					{
						Address = l.Address,
						Assigned = l.Assigned,
						BusinessName = l.BusinessName,
						Campaign = l.Campaign,
						Carrier = l.Carrier,
						Category = l.Category,
						City = l.City,
						ContactFirstname = l.ContactFirstname,
						ContactLastname = l.ContactLastname,
						ContactTitle = l.ContactTitle,
						Email = l.Email,
						FaxNumber = l.FaxNumber,
						LeadsId = l.LeadsId,
						LeadType = l.LeadType,
						LecAccount = l.LecAccount,
						MobileNumber = l.MobileNumber,
						PhoneNumber = l.PhoneNumber,
						State = l.State,
						Status = l.Status,
						Suite = l.Suite,
						UploadDate = l.UploadDate,
						Zip = l.Zip,
						StatusDescription = c.CodeDescription,
						LeadTypeDescription = ltc.CodeDescription,
						AssignedUser = $"{u.FirstName} {u.LastName}",
						OrderId = ord.OrderId
					}).OrderBy(c => c.BusinessName).ToList();
		}


		[NonAction]
		private List<LeadsVM> GetLeadsData(IQueryable<Leads> leadsList, IQueryable<Codes> leadStatusCodes, IQueryable<Codes> leadTypeCodes, IQueryable<Models.User> users, IQueryable<Call> callsList)
		{
			return (from l in leadsList
					join c in leadStatusCodes on l.Status equals c.CodeValue
					join u in users on l.UserId equals u.UserId
					join ltc in leadTypeCodes on l.LeadType.ToString().ToLower() equals ltc.CodeValue into ltcGroup
					from ltc in ltcGroup.DefaultIfEmpty()
					select new LeadsVM()
					{
						Address = l.Address,
						Assigned = l.Assigned,
						BusinessName = l.BusinessName,
						Campaign = l.Campaign,
						Carrier = l.Carrier,
						Category = l.Category,
						City = l.City,
						ContactFirstname = l.ContactFirstname,
						ContactLastname = l.ContactLastname,
						ContactTitle = l.ContactTitle,
						Email = l.Email,
						FaxNumber = l.FaxNumber,
						LeadsId = l.LeadsId,
						LeadType = l.LeadType,
						LecAccount = l.LecAccount,
						MobileNumber = l.MobileNumber,
						PhoneNumber = l.PhoneNumber,
						State = l.State,
						Status = l.Status,
						Suite = l.Suite,
						UploadDate = l.UploadDate,
						Zip = l.Zip,
						StatusDescription = c.CodeDescription,
						LeadTypeDescription = ltc.CodeDescription,
						AssignedUser = $"{u.FirstName} {u.LastName}"
					}).OrderBy(c => c.BusinessName).ToList();
		}

		/// <summary>
		/// Filters leads list based on user's role
		/// 1. For role Lead Admin: returns all the leads
		/// 2. For role Sales Lead: returns leads to a specific campaign which the user manages
		/// 3. For role Sales executive: returns leads assigned to this user
		/// </summary>
		/// <param name="sessionUser"></param>
		/// <param name="userRoles"></param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		[NonAction]
		private IQueryable<Leads> GetLeadsListBasedOnRole(long sessionUser, List<Role> userRoles)
		{
			if (userRoles.Any(c => c.RoleName == Roles.Lead_Admin))
			{
				var leadsList = _dbContext.Leads.Where(s => s.Status != RawLeadStatus.Drop);
				return leadsList;
			}
			else if (userRoles.Any(c => c.RoleName == Roles.Sales_Lead))
			{
				var campaignDetails = _unitOfWork.Campaign.GetFirstOrDefault(c => c.CampaignSupervisor == sessionUser);
				if (campaignDetails != null)
				{
					var leadsList = _dbContext.Leads.Where(s => s.Campaign == campaignDetails.CampaignName && s.Status != RawLeadStatus.Drop);
					return leadsList;
				}
				else
				{
					var leadsList = _dbContext.Leads.Where(s => s.UserId == -1); // just to return empty list of Queryable type
					return leadsList;
				}
			}
			else if (userRoles.Any(c => c.RoleName == Roles.Sales_Executive))
			{
				var leadsList = _dbContext.Leads
				   .Where(s => s.UserId == sessionUser && s.Status != RawLeadStatus.Drop);
				return leadsList;
			}
			else
			{
				throw new Exception($"user id:{sessionUser} does not have appropriate role assigned to access this page");
			}

		}
		[NonAction]
		public List<CodeDto> CodesList(string codeType, List<Codes> codesList)
		{
			try
			{
				var resultSet = (from status in codesList.Where(c => c.CodeType == codeType && c.Status == Status.Active).ToList()// _unitofwork.Codes.GetAll(s => s.CodeType == "STATUS" && s.Status == "ACTV")
								 orderby status.CodeValue
								 select new CodeDto()
								 {
									 CodeId = status.CodeId,
									 CodeValue = status.CodeValue,
									 CodeDescription = status.CodeDescription,
									 CodeComments = status.CodeComments,
								 }).ToList();

				return resultSet;
			}
			catch (Exception e)
			{
				_logger.LogError(e.Message);
				throw new Exception($"Exception in GetLeadStatusCount method");
			}

		}


		//Get all TSD to set in dropdown - by ajax call
		[HttpGet]
		[Route("/PostSales/GetAllTSD")]
		public ResponseDto GetAllTSD()
		{
			try
			{
				var resultSet = _unitOfWork.TechnologyServiceCarrier.GetAll();

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = resultSet
				};
			}
			catch (Exception e)
			{
				_logger.LogError(string.Format("{0}:{1}", "GetAllTSD", e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message }
				};
			}
		}

		//Get all TSD to set in dropdown - by ajax call
		[HttpPost]
		[Route("/PostSales/CheckPreviousOrderStaus")]
		public ResponseDto CheckPreviousOrderStaus([FromBody] long leadId)
		{
			try
			{
				var lastOrderStausOfTheLead = _unitOfWork.OrderStatus.GetAll(c => c.LeadsId == leadId).ToList().
								OrderByDescending(c => c.StatusChangeDate).FirstOrDefault();

				if (lastOrderStausOfTheLead == null)
				{
					throw new Exception("Last Order Staus Of The Lead not found");
				}
				var resultSet = "";
				if (lastOrderStausOfTheLead.Status == LeadStatus.PendSchedule)
				{
					resultSet = "Success";
				}

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = resultSet
				};
			}
			catch (Exception e)
			{
				_logger.LogError(string.Format("{0}:{1}", "GetAllTSD", e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message }
				};
			}
		}



		[HttpPost]
		[Route("/PostSales/UpdateLeadsStatusPostSales")]

		public ResponseDto UpdateLeadsStatusPostSales(PostSalesStatusVM postSalesStatusObj)
		{
			try
			{
				DateTimeOffset istDateTime = DateTime.UtcNow;
				var leadsData = _unitOfWork.Leads.GetAll(c => c.LeadsId == postSalesStatusObj.leadId).FirstOrDefault();
				if (leadsData == null)
				{
					throw new ArgumentNullException(ErrorMessages.Lead_Not_Found.Replace("{}", postSalesStatusObj.leadId.ToString()));
				}
				if (postSalesStatusObj == null)
				{
					throw new ArgumentNullException(nameof(postSalesStatusObj));
				}
				var sessionUser = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimsKey.UserId)?.Value);
				if (sessionUser == 0)
					throw new InvalidDataException(ErrorMessages.Session_User_Not_Available);

				DateTime? setNextCallReminder = null;
				if (postSalesStatusObj.reminderTime != null)
				{
					string? leadTimezone = _unitOfWork.States.GetAll(c => c.StateCode == leadsData.State).Select(c => c.StateTimezone).FirstOrDefault();
					if (leadTimezone == null)
						throw new InvalidDataException("selectedDateTime assigned null value");

					istDateTime = ConvertAnyTimezoneToUTC((DateTime)postSalesStatusObj.reminderTime, leadTimezone);
					setNextCallReminder = istDateTime.DateTime;
					if (setNextCallReminder < DateTime.Now)
						setNextCallReminder = DateTime.Now;
				}

				var orderId = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);
				var reminderId = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);
				var orderStatusId = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);
				if (postSalesStatusObj.orderId != null)
				{
					var recordFromOrderTable = _unitOfWork.Orders.GetFirstOrDefault(c => c.OrderId == postSalesStatusObj.orderId);
					if (recordFromOrderTable != null)
					{
						recordFromOrderTable.UserId = sessionUser;
						recordFromOrderTable.TsdId = postSalesStatusObj.tsd;
						recordFromOrderTable.ConfirmationNumber = postSalesStatusObj.confirmationNumber;
						recordFromOrderTable.AccountNumber = postSalesStatusObj.accountNumber;
						recordFromOrderTable.CostOfConstruction = postSalesStatusObj.costOfConstruction;
						recordFromOrderTable.ReasonToCancelOrder = postSalesStatusObj.reasonToCancelOrder;
						recordFromOrderTable.ScheduledDateAndTime = postSalesStatusObj.scheduledDateAndTime;
						recordFromOrderTable.Status = Status.Active;
						recordFromOrderTable.StatusChangeDate = DateTime.Now;
					}
					_unitOfWork.OrderStatus.Add(new OrderStatus
					{
						OrderStatusId = orderStatusId,
						LeadsId = postSalesStatusObj.leadId,
						UserId = sessionUser,
						LeadsNotes = postSalesStatusObj.callNotes,
						Status = postSalesStatusObj.callStatus,
						StatusChangeDate = DateTime.Now,
						OrderId = recordFromOrderTable.OrderId,
						ReminderId = reminderId,
					});
					//	_unitOfWork.Save();
				}
				else
				{
					_unitOfWork.Orders.Add(new Order
					{
						OrderId = orderId,
						UserId = sessionUser,
						LeadId = postSalesStatusObj.leadId,
						TsdId = postSalesStatusObj.tsd,
						TypeOfOrder = null, //postSalesStatusObj.typeOfOrder,
						ConfirmationNumber = postSalesStatusObj.confirmationNumber,
						AccountNumber = postSalesStatusObj.accountNumber,
						CostOfConstruction = postSalesStatusObj.costOfConstruction,
						ReasonToCancelOrder = postSalesStatusObj.reasonToCancelOrder,
						ScheduledDateAndTime = postSalesStatusObj.scheduledDateAndTime,
						Status = Status.Active,
						StatusChangeDate = DateTime.Now,
					});
					_unitOfWork.OrderStatus.Add(new OrderStatus
					{
						OrderStatusId = orderStatusId,
						LeadsId = postSalesStatusObj.leadId,
						UserId = sessionUser,
						LeadsNotes = postSalesStatusObj.callNotes,
						Status = postSalesStatusObj.callStatus,
						StatusChangeDate = DateTime.Now,
						OrderId = orderId,
						ReminderId = reminderId,
					});

				}
				_unitOfWork.Reminders.Add(new Reminder
				{
					ReminderId = reminderId,
					UserId = sessionUser,
					LeadId = postSalesStatusObj.leadId,
					ReminderType = postSalesStatusObj.reminderType,
					ReminderStatus = Status.Pending,
					ReminderTime = postSalesStatusObj.reminderTime,
					NextCallTime = setNextCallReminder,
					Status = Status.Active,
					StatusChangeDate = DateTime.Now,
				});


				_unitOfWork.Save();

				//code values for Post Sales
				var codeValuesForPostSales = new List<string>() { LeadStatus.Installed, LeadStatus.Scheduled ,
																  LeadStatus.Order_Cancelled,LeadStatus.PendSchedule,
																  LeadStatus.Construction, LeadStatus.WorkInProgress,
																  LeadStatus.Order
																};
				IQueryable<Leads> leadsList = null;

				leadsList = _dbContext.Leads.Where(c => c.LeadsId == postSalesStatusObj.leadId).AsQueryable();

				IQueryable<Codes> codes = _dbContext.Codes.Where(s => codeValuesForPostSales.Contains(s.CodeValue) && s.Status == Status.Active && s.CodeType == "LEAD_STATUS").AsQueryable();

				IQueryable<Codes> leadTypeCodes = _dbContext.Codes.Where(s => s.CodeType == CodeTypes.Lead_Type).AsQueryable();

				IQueryable<User> users = _dbContext.User.Where(u => leadsList.ToList().Select(l => l.UserId).Contains(u.UserId)).AsQueryable();

				IQueryable<Call> calls = _dbContext.Call.Where(c => leadsList.ToList().Select(l => l.LeadsId.ToString()).Contains(c.LeadId.ToString())).AsQueryable();

				List<LeadsVM> resultSet = GetLeadsData(leadsList, codes, leadTypeCodes, users, calls);
				return new ResponseDto()
				{
					IsSuccess = true,
					DisplayMessage = SuccessMessages.Status_Updated,
					Result = resultSet.First(),
				};
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}
		}

		[HttpPost]
		[Route("/PostSales/UpdateSalesOrderDetailsPostSales")]

		public ResponseDto UpdateSalesOrderDetailsPostSales(SalesOrderDetailsForPostSalesVM salesOrderDeatilsObj)
		{
			try
			{
				if (salesOrderDeatilsObj == null)
				{
					throw new ArgumentNullException(nameof(salesOrderDeatilsObj));
				}
				if (salesOrderDeatilsObj.saleOrders != null)
				{
					if (salesOrderDeatilsObj.saleOrders.Count > 0)
					{
						AddSaleOrders(salesOrderDeatilsObj);
					}
				}
				_unitOfWork.Save();

				//code values for Post Sales
				var codeValuesForPostSales = new List<string>() {   LeadStatus.Sale,
														LeadStatus.Installed, LeadStatus.Scheduled ,
														LeadStatus.Order_Cancelled,LeadStatus.PendSchedule,
														LeadStatus.Construction, LeadStatus.WorkInProgress,
														LeadStatus.Order
													};
				IQueryable<Leads> leadsList = null;

				leadsList = _dbContext.Leads.Where(c => c.LeadsId == salesOrderDeatilsObj.leadId).AsQueryable();

				IQueryable<Codes> codes = _dbContext.Codes.Where(s => codeValuesForPostSales.Contains(s.CodeValue) && s.Status == Status.Active && s.CodeType == "LEAD_STATUS").AsQueryable();

				IQueryable<Codes> leadTypeCodes = _dbContext.Codes.Where(s => s.CodeType == CodeTypes.Lead_Type).AsQueryable();

				IQueryable<User> users = _dbContext.User.Where(u => leadsList.ToList().Select(l => l.UserId).Contains(u.UserId)).AsQueryable();

				IQueryable<Call> calls = _dbContext.Call.Where(c => leadsList.ToList().Select(l => l.LeadsId.ToString()).Contains(c.LeadId.ToString())).AsQueryable();

				List<LeadsVM> resultSet = GetLeadsData(leadsList, codes, leadTypeCodes, users, calls);
				return new ResponseDto()
				{
					IsSuccess = true,
					DisplayMessage = SuccessMessages.Status_Updated,
					Result = resultSet.First(),
				};
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}
		}
		[NonAction]
		public void AddSaleOrders(SalesOrderDetailsForPostSalesVM salesOrderDeatilsObj)
		{
			try
			{
				var sessionUser = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimsKey.UserId)?.Value);
				if (sessionUser == 0)
					throw new InvalidDataException(ErrorMessages.Session_User_Not_Available);
				long orderHeaderId = (long)salesOrderDeatilsObj.saleOrderHeaderId;
				if (orderHeaderId == 0 || orderHeaderId == null)
				{
					var campaignList = _unitOfWork.Campaign.GetAll(c => c.Status == Status.Active).ToList();
					var saleConfiguration = _unitOfWork.SalesOrderConfiguration.GetAll(c => c.InformationType != null).ToList();
					var saleOrderHeaderId = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);
					var campaignName = _unitOfWork.Leads.GetAll(l => l.LeadsId == salesOrderDeatilsObj.leadId).Select(l => l.Campaign).FirstOrDefault();
					_unitOfWork.SalesOrderHeader.Add(new SalesOrderHeader
					{
						SalesOrderHeaderId = saleOrderHeaderId,
						LeadsId = salesOrderDeatilsObj.leadId,
						UserId = sessionUser,
						CampaignId = campaignList.Where(s => s.CampaignName == campaignName).Select(s => s.CampaignId).FirstOrDefault()
					});
					foreach (var sale in salesOrderDeatilsObj.saleOrders)
					{
						var saleOrderHeaderDetailId = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);

						_unitOfWork.SalesOrderHeaderDetail.Add(new SalesOrderHeaderDetail
						{
							SalesOrderHeaderDetailsId = saleOrderHeaderDetailId,
							SalesOrderHeaderId = saleOrderHeaderId,
							InformationType = sale.configurationId != null ? saleConfiguration.Where(s => s.SalesOrderConfigurationId == sale.configurationId).Select(s => s.InformationType).FirstOrDefault() : null,
							InformationValue = sale.InformationValue
						});
					}
				}
				else
				{
					foreach (var sale in salesOrderDeatilsObj.saleOrders)
					{
						var salesOrderDetails = _unitOfWork.SalesOrderHeaderDetail.GetAll(c => c.SalesOrderHeaderId == orderHeaderId).ToList();
						if (salesOrderDetails != null)
						{
							var salesInformationDetail = salesOrderDetails.Where(c => c.InformationType == sale.InformationType).FirstOrDefault();
							if (salesInformationDetail != null)
							{
								salesInformationDetail.InformationValue = sale.InformationValue;
							}
							else
							{
								_logger.LogInformation($"Could not find {sale.InformationType} information type in sales order header details table");
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
			}
		}

		public DateTimeOffset ConvertAnyTimezoneToUTC(DateTime? selectedDateTimeWithTimezone, string? clientTimezone)
		{
			// Convert the selectedDateTimeWithTimezone to UTC
			if (selectedDateTimeWithTimezone == null)
				throw new Exception("ConvertAnyTimezoneToUTC: null value passed as Argument");

			DateTimeOffset utcDateTimeOffset = TimeZoneInfo.ConvertTimeToUtc((DateTime)selectedDateTimeWithTimezone, TimeZoneInfo.FindSystemTimeZoneById(clientTimezone));
			return utcDateTimeOffset;
		}


		[HttpGet]
		[Route("/PostSales/GetPostSalesHistoryDetails")]
		public ResponseDto GetPostSalesHistoryDetails([FromBody] long leadId)
		{
			try
			{
				var states = _unitOfWork.States.GetAll();
				var leadDetails = _unitOfWork.Leads.GetAll(a => a.LeadsId == leadId).ToList();
				var leadStatusDetailsList = _unitOfWork.OrderStatus.GetAll(a => a.LeadsId == leadId);
				var codeForStatus = _unitOfWork.Codes.GetAll(c => c.CodeType == "LEAD_STATUS");
				var user = _unitOfWork.User.GetAll(a => a.Status.Equals(Status.Active));
				var result = (from l in leadDetails
							  join u in user on l.UserId equals u.UserId
							  join ls in leadStatusDetailsList on l.LeadsId equals ls.LeadsId
							  join c in codeForStatus on ls.Status equals c.CodeValue
							  join usr in user on ls.UserId equals usr.UserId
							  join s in states on l.State equals s.StateCode

							  select new LeadsStatusVM
							  {
								  TimeZone = s.StateTimezone,
								  LeadStatusId = ls.OrderStatusId,
								  PostSalePersonName = usr.FirstName + " " + usr.LastName,
								  PostSalePersonNotes = ls.LeadsNotes,
								  Time = ls.StatusChangeDate != null ? ConvertUTCToUserTimezone((DateTime)ls.StatusChangeDate, s.StateTimezone) : null,
								  Status = c.CodeDescription,
							  }).OrderByDescending(ls => ls.Time).ToList();

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = result
				};
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}

		}
		private DateTime ConvertUTCToUserTimezone(DateTime utcDateTime, string userTimezone)
		{
			// Convert the UTC DateTime to the user's time zone
			TimeZoneInfo userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(userTimezone);

			DateTime userDateTimeOffset = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime.ToUniversalTime(), userTimeZone);

			return userDateTimeOffset;
		}

		[HttpGet]
		[Route("/PostSales/GetReminderData")]
		public ResponseDto GetReminderData()
		{
			try
			{
				var sessionUser = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimsKey.UserId)?.Value);
				if (sessionUser == 0)
					throw new InvalidDataException(ErrorMessages.Session_User_Not_Available);

				var yesterday = DateTime.Now.AddDays(-1);

				var reminderDetailsList = _unitOfWork.Reminders
					.GetAll(a => a.Status.Equals(Status.Active) && a.UserId == sessionUser && a.NextCallTime >= yesterday)
					.ToList();

				var leadsIds = reminderDetailsList.Select(r => r.LeadId).ToList();
				var reminderIds = reminderDetailsList.Select(r => r.ReminderId).ToList();

				var filteredOrderStatusList = _unitOfWork.OrderStatus
					.GetAll(a => leadsIds.Contains(a.LeadsId) && reminderIds.Contains((long)a.ReminderId))
					.ToList();

				var orderStatusDictionary = filteredOrderStatusList
					.GroupBy(a => a.ReminderId)
					.ToDictionary(
						group => group.Key,
						group => group.OrderByDescending(a => a.OrderStatusId).FirstOrDefault()?.Status
					);

				var codes = _unitOfWork.Codes.GetAll(a => a.CodeType == CodeTypes.Lead_Status);
				var reminderFromCode = _unitOfWork.Codes.GetAll(a => a.CodeType == CodeTypes.Reminder_Type);
				var states = _unitOfWork.States.GetAll().ToList();
				var leadDetails = _unitOfWork.Leads.GetAll().ToList();

				var result = (
					from rl in reminderDetailsList.Where(a => a.ReminderStatus != null).OrderBy(a => a.NextCallTime).Take(200)
					join l in leadDetails on rl.LeadId equals l.LeadsId
					join s in states on l.State equals s.StateCode
					join statusPair in orderStatusDictionary on rl.ReminderId equals statusPair.Key
					join c in codes on statusPair.Value equals c.CodeValue into codesGroup
					from c in codesGroup.DefaultIfEmpty()

					join code in reminderFromCode on rl.ReminderType equals code.CodeValue into codeGroup
					from code in codeGroup.DefaultIfEmpty()
					select new ReminderVM
					{
						LeadId = rl.LeadId,
						UserId = rl.UserId,
						TimeZone = s.StateTimezone,
						CallTime = ConvertUTCToUserTimezone((DateTime)rl.StatusChangeDate, s.StateTimezone),
						Status = rl.Status,
						ReminderType = code.CodeDescription,//rl.ReminderType,
						CallNotes = filteredOrderStatusList.FirstOrDefault(os => os.ReminderId == rl.ReminderId)?.LeadsNotes,
						CallStatus = c.CodeDescription,
						NextCallTime = ConvertUTCToUserTimezone((DateTime)rl.NextCallTime, s.StateTimezone),
						StatusChangeDate = rl.StatusChangeDate,
						ReminderStatus = rl.ReminderStatus,
						BusinessName = l.BusinessName,
						Campaign = l.Campaign,
						Carrier = l.Carrier,
						City = l.City,
						State = l.State,
						Zip = l.Zip,
						PhoneNumber = l.PhoneNumber,
						MobileNumber = l.MobileNumber,
						TimezoneState = TZConvert.WindowsToIana(s.StateTimezone),
						OrderStatusId = filteredOrderStatusList.FirstOrDefault(os => os.ReminderId == rl.ReminderId)?.OrderStatusId,
						ReminderId = statusPair.Key
					}).ToList();

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = result,
				};
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError($"{actionName}:{e}");

				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}
		}

		[HttpPost]
		[Route("/PostSales/UpdateReminderStatus")]
		public ResponseDto UpdateReminderStatus(List<UpdateReminderStatus> reminderData)
		{
			try
			{
				long sessionUserId = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimsKey.UserId)?.Value);
				foreach (var reminder in reminderData)
				{
					var reminderList = _unitOfWork.Reminders.GetFirstOrDefault(a => a.ReminderId == reminder.ReminderId);
					if (reminderList != null)
					{
						reminderList.ReminderStatus = Status.Close;
						reminderList.StatusChangeDate = DateTime.Now;
						reminderList.UserId = sessionUserId; //session user
					}
				}
				_unitOfWork.Save();
				return new ResponseDto()
				{
					IsSuccess = true,
					DisplayMessage = SuccessMessages.Reminder_Updated,
				};

			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}
		}

		[HttpGet]
		[Route("/PostSales/GetCallRecordingDetails")]
		public async Task<ResponseDto> GetCallRecordingDetails([FromBody] long leadId)
		{
			_logger.LogInformation($"Executing PostSales API controller - GetCallRecordingDetails() ");
			try
			{
				var callRecording = _unitOfWork.CallRecordings.GetAll(s => s.LeadsId == leadId && s.CallDuration > 60
																	&& s.UploadedFileName != null).ToList();

				var leadDetails = _unitOfWork.Leads.GetAll(a => a.LeadsId == leadId).FirstOrDefault();
				var states = _unitOfWork.States.GetAll(s => s.StateCode == leadDetails.State).First();
				var user = _unitOfWork.User.GetAll(a => a.Status.Equals(Status.Active) && a.UserId == leadDetails.UserId);

				var resultSet = (from cr in callRecording
								 join u in user on leadDetails.UserId equals u.UserId into uGroup
								 from u in uGroup.DefaultIfEmpty()
								 select new CallRecordingsVM
								 {

									 TimeZone = states?.StateTimezone,
									 LeadId = leadId,
									 LeadName = leadDetails.BusinessName,
									 UserId = leadDetails?.UserId,
									 UserName = (u != null) ? u.FirstName + " " + u.LastName : string.Empty,
									 CallToNumber = cr.CallToNumber,
									 RecordingId = cr.RcCallRecordingId,
									 RecordingTime = (cr.CallStartTime != null) ? ConvertUTCToUserTimezone((DateTime)cr.CallStartTime, states.StateTimezone) : null,
									 FileName = (cr.UploadedFileName != null) ? cr.UploadedFileName : null,
									 Recording = (cr.UploadedFileName != null) ? GetCallRecording(cr.UploadedFileName).Result : null // Use .Result to await the result
								 }).OrderByDescending(s => s.RecordingTime).Take(50).ToList();

				return new ResponseDto()
				{
					IsSuccess = true,
					Result = resultSet
				};
			}
			catch (Exception e)
			{
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, e));
				//return StatusCode(500);
				return new ResponseDto()
				{
					IsSuccess = false,
					DisplayMessage = ErrorMessages.Internal_Server_Error,
					ErrorMessages = new List<string>() { e.Message },
				};
			}
		}
		public async Task<byte[]> GetCallRecording(string? uploadedFileName)
		{
			_logger.LogInformation($"Executing sales processing QA API controller - GetCallRecording() ");
			var client = new HttpClient();
			var azureApiUrl = "http://20.232.146.38/api/Storage/download?"; // Replace with the actual Azure API URL
			byte[] callRecordingData = null;
			try
			{
				var azureContainerName = containerName; // Replace with your Azure container name
				if (azureContainerName == null || uploadedFileName == null)
					throw new ArgumentNullException("Container name or file name is null");

				var apiRequest = new HttpRequestMessage(HttpMethod.Get, azureApiUrl);
				//apiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Request.Headers.Authorization.ToString().Substring(7));

				var queryParams = new List<KeyValuePair<string, string>>
				{
					new KeyValuePair<string, string>("fileName", uploadedFileName),
					new KeyValuePair<string, string>("containerName", azureContainerName)
				};

				apiRequest.RequestUri = new Uri($"{apiRequest.RequestUri}{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

				HttpResponseMessage response = await client.SendAsync(apiRequest);

				if (response.IsSuccessStatusCode)
				{
					var responseContent = await response.Content.ReadAsStringAsync();
					JObject jsonObject = JObject.Parse(responseContent);

					// Step 2: Extract content data from the JSON object
					string contentBase64 = jsonObject["blob"]["content"].Value<string>();

					// Step 3: Convert content data to a byte array
					callRecordingData = Convert.FromBase64String(contentBase64);

					return callRecordingData;
				}
				else
				{
					throw new InvalidOperationException("API request failed: " + response.ReasonPhrase);
				}
			}
			catch (Exception ex)
			{
				// Handle any other exceptions
				string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
				_logger.LogError(string.Format("{0}:{1}", actionName, ex));
				throw; // Re-throw the exception for proper error handling
			}
		}
	}
}
