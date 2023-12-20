using EntityFrameworkCore.BaseRepository.IRepository;
using EntityFrameworkCore.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Requirement_API.HelperClasses;
using Entities;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Entities.ViewModel;


namespace Requirement_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RequirementController : Controller
    {

        private readonly IUnitOfWork _unitOfWork;

        private readonly ApplicationDbContext _db;
        public ILogger<RequirementController> _logger { get; set; }

        private readonly IConfiguration _configuration;
        bool nextIdCalled = false;
        long gReqSectionId = 0;


        public RequirementController(IUnitOfWork unitOfWork, ILogger<RequirementController> logger, ApplicationDbContext db, IConfiguration configuration)

        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _db = db;
            _configuration = configuration;
        }

        //codeComment.method.GetNextSeqNumber.description = "function to generate Unique Identifiers"
        private long GetNextSeqNumber()
        {
            //long seq = _unitOfWork.SP_Call.Single<long>(SD.Proc_Get_Next_Seq_No);

            //return seq;


            try
            {
                string seqNumber = "";
                string npgCommand = SD.Proc_Get_Next_Seq_No;

                string? connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new NpgsqlCommand(npgCommand, connection))
                    {
                        NpgsqlDataReader dataReader = command.ExecuteReader();

                        while (dataReader.Read())
                        {
                            seqNumber = dataReader[0].ToString();
                        }
                    }
                }

                return long.Parse(seqNumber);
            }
            catch (Exception ex)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);
                throw;
            }
        }


        //codeComment.method.ValidateInputObject.description = "function to validate input object"
        public List<string> ValidateInputObject()
        {
            try
            {
                var errorList = new List<string>();
                if (!ModelState.IsValid)
                {
                    var errors = new List<string>();
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            errors.Add(error.ErrorMessage);
                        }
                    }

                    errorList = errors;
                    return errorList;
                }

                else
                {
                    return errorList;
                }
            }
            catch (Exception ex)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);

                throw;
            }
        }

        //codeComment.method.SaveRequirementDetails.description = "function to save requirement details"
        [HttpPost("SaveRequirementDetails")]
        [Route("[action]")]
        public IActionResult SaveRequirementDetails(RequirementSectionObject? parRequirementSectionObject/* List<BlockObject> blockObjects*/)

        {
            ResultSet resultSet = new ResultSet();
            RequirementSectionObject objToReturn = new RequirementSectionObject();
            int statusCode = 500;

            try
            {
                var errorList = ValidateInputObject();
                var validationFlag = (errorList.Count > 0) ? false : true;

                var mainStatusCode = 500;
                var statusMsg = " ";

                if (errorList.Count == 0 && parRequirementSectionObject != null)
                {
                    var isSaved = AddRequirementSectionDetails(parRequirementSectionObject);

                    if (isSaved)
                    {
                        GetRequirementSectionDetailsObject getRequirementSectionDetailsObject = new GetRequirementSectionDetailsObject
                        {
                            ReqSectionId = gReqSectionId,
                            ApiVersion = "1.0",
                            UserId = 111
                        };

                        var jsonResult = GetRequirementSectionDetails(getRequirementSectionDetailsObject);

                        objToReturn = jsonResult;
                        statusCode = 200;

                    }
                    else
                    {
                        objToReturn = parRequirementSectionObject;
                    }
                }

            }
            catch (Exception ex)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);
                throw;
            }

            resultSet.reqSectionObjString = objToReturn;
            resultSet.statusCode = statusCode;
            return Json(resultSet);
        }

        //codeComment.method.AddRequirementSectionDetails.description = "helper function to add requirement details to db"
        public bool AddRequirementSectionDetails(RequirementSectionObject parRequirementSectionObject)
        {
            try
            {
                //Generating requirement section Id
                long reqSectionId = GetNextSeqNumber();

                //Adding req section object to MaintenanceExecution table
                ReqSection reqSectionObj = new ReqSection
                {
                    ReqSectionId = reqSectionId,
                    ReqHeaderId = 251,
                    ParentSectionId = Convert.ToInt64(parRequirementSectionObject.ParentSectionId),
                    SectionLabel = parRequirementSectionObject.SectionLabel,
                    SectionDescription = parRequirementSectionObject.SectionDescription,
                    //RequirementNumber = parRequirementSectionObject.RequirementNumber,
                    RequirementNumber = "1",
                    CreatedBy = Convert.ToInt64(parRequirementSectionObject.CreatedBy),
                    CreateDate = DateTime.Now,
                    Status = "ACTV",
                    StatusChangeDate = DateTime.Now,

                };
                _unitOfWork.ReqSections.Add(reqSectionObj);
                _unitOfWork.Save();



                if (parRequirementSectionObject.ReqObjList != null && parRequirementSectionObject.ReqObjList.Count() != 0)
                {
                    //Adding maintenance execution action object to MaintenanceExecutionAction table
                    foreach (var req in parRequirementSectionObject.ReqObjList)
                    {
                        //Generating Req detail Id
                        long reqDetailId = GetNextSeqNumber();

                        ReqDetail reqDetailsObj = new ReqDetail
                        {
                            ReqDetailId = reqDetailId,
                            ReqSectionId = reqSectionId,
                            ParentReqDetailId = req.ParentReqDetailId,
                            ReqNumber = req.ReqNumber,
                            CreatedBy = Convert.ToInt64(parRequirementSectionObject.CreatedBy),
                            CreateDate = DateTime.Now,
                            Status = req.Status,
                            StatusChangeDate = DateTime.Now,
                            ReqText = req.ReqText,
                            ReqDetailEditorTimestamp = req.ReqDetailEditorTimestamp,
                            ReqDetailEditorVersion = req.ReqDetailEditorVersion,
                            ReqContainerId = req.ReqContainerId,
                            ReqParentContainerId = req.ReqParentContainerId,
                            Comments = req.Comments,


                        };
                        _unitOfWork.ReqDetails.Add(reqDetailsObj);
                    }


                }
                _unitOfWork.Save();
                gReqSectionId = reqSectionId;

                return true;
            }
            catch (Exception ex)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);
                return false;
            }

        }






        //codeComment.method.UpdateRequirementSectionAndDetails.description = "helper function to update requirement details to db"
        //[Authorize(Roles = "Execute")]
        public bool UpdateRequirementSectionAndDetails(UpdateObject parUpdateRequirementsObj)
        {
            try
            {
                RequirementSectionObject requirementSectionObject = parUpdateRequirementsObj.RequirementSectionObject;

                ReqSection recordFromDb = parUpdateRequirementsObj.ReqSectionRecord;

                //Updating data in Maintenance Execution Table
                if (requirementSectionObject.StatusChangeDate != null)
                {
                    recordFromDb.StatusChangeDate = requirementSectionObject.StatusChangeDate;
                }
                //Updating data in Maintenance Execution Table
                if (requirementSectionObject.Status != null)
                {
                    recordFromDb.Status = requirementSectionObject.Status;
                }
                if (requirementSectionObject.SectionLabel != null)
                {
                    recordFromDb.SectionLabel = requirementSectionObject.SectionLabel;
                }
                if (requirementSectionObject.SectionDescription != null)
                {
                    recordFromDb.SectionDescription = requirementSectionObject.SectionDescription;
                }
                if (requirementSectionObject.Comments != null)
                {
                    recordFromDb.Comments = requirementSectionObject.Comments;
                }

                _unitOfWork.ReqSections.Update(recordFromDb);


                if (requirementSectionObject.ReqObjList != null && requirementSectionObject.ReqObjList.Count() != 0)
                {
                    //Updating data in Maintenance Execution Action Table
                    foreach (var req in requirementSectionObject.ReqObjList)
                    {
                        var reqDetails = _unitOfWork.ReqDetails.Get(rec => rec.ReqDetailId == Convert.ToInt64(req.ReqDetailId)).FirstOrDefault();

                        if (reqDetails != null)
                        {
                            if (req.ReqNumber != null)
                            {
                                reqDetails.ReqNumber = req.ReqNumber;
                            }
                            if (req.ReqText != null)
                            {
                                reqDetails.ReqText = req.ReqText;
                            }
                            if (req.ReqDetailEditorTimestamp != null)
                            {
                                reqDetails.ReqDetailEditorTimestamp = req.ReqDetailEditorTimestamp;
                            }
                            if (req.ReqDetailEditorVersion != null)
                            {
                                reqDetails.ReqDetailEditorVersion = req.ReqDetailEditorVersion;
                            }
                            if (req.ParentReqDetailId != null)
                            {
                                reqDetails.ParentReqDetailId = req.ParentReqDetailId;
                            }
                            if (req.Status != null)
                            {
                                reqDetails.Status = req.Status;
                            }
                            if (req.StatusChangeDate != null)
                            {
                                reqDetails.StatusChangeDate = req.StatusChangeDate;
                            }
                            if (req.Comments != null)
                            {
                                reqDetails.Comments = req.Comments;
                            }

                            reqDetails.StatusChangeDate = DateTime.Now;

                            _unitOfWork.ReqDetails.Update(reqDetails);
                        }
                        else
                        {
                            if (req.ReqText != null && req.ReqText != "")
                            {
                                long reqDetailId = GetNextSeqNumber();

                                var reqDetailsObj = new ReqDetail
                                {
                                    ReqSectionId = req.ReqSectionId,
                                    ReqDetailId = reqDetailId,
                                    ReqText = req.ReqText,
                                    ReqDetailEditorTimestamp = req.ReqDetailEditorTimestamp,
                                    ReqDetailEditorVersion = req.ReqDetailEditorVersion,
                                    ParentReqDetailId = req.ParentReqDetailId,
                                    ReqNumber = req.ReqNumber,
                                    CreateDate = DateTime.Now,
                                    CreatedBy = req.CreatedBy,
                                    Status = "ACTV",
                                    StatusChangeDate = DateTime.Now,
                                    Comments = req.Comments,

                                };
                                _unitOfWork.ReqDetails.Add(reqDetailsObj);
                            }

                        }

                    }

                }

                return true;
            }
            catch (Exception ex)
            {
                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);
                return false;
            }



        }

        //codeComment.method.GetRequirementSectionDetails.description = "helper function to get requirement details from db"
        // [HttpPost]
        public RequirementSectionObject GetRequirementSectionDetails([FromBody]/* RequirementSectionDetailObject*/ GetRequirementSectionDetailsObject inputObj)
        {
            RequirementSectionObject reqSectionDetailsObj = new RequirementSectionObject();

            try
            {
                var apiVersion = inputObj.ApiVersion;
                var reqSectionId = inputObj.ReqSectionId;
                var userId = Convert.ToInt64(inputObj.UserId);
                List<RequirementDetailsObject> reqDetailsObjList = new List<RequirementDetailsObject>();
                List<EditorDetailsObj> editorDetailsObj = new List<EditorDetailsObj>();

                if (reqSectionId != null)
                {

                    var requirementSectionData = _unitOfWork.ReqSections.Get(rec => rec.ReqSectionId == reqSectionId && rec.Status == "ACTV").FirstOrDefault();


                    //check
                    if (requirementSectionData != null)
                    {
                        var requirementDetailsData = _unitOfWork.ReqDetails.GetAll(rec => rec.ReqSectionId == reqSectionId && rec.Status == "ACTV").ToList();

                        if (requirementDetailsData.Count() != 0)
                        {

                            foreach (var req in requirementDetailsData)
                            {

                                editorDetailsObj.Add(new EditorDetailsObj
                                {
                                    editorId = req.ReqContainerId,
                                    parentId = req.ReqParentContainerId.ToString(),
                                    reqNumber = req.ReqNumber,
                                    comments = req.Comments,
                                    editorInstancePromise = new EditorDataObject
                                    {
                                        time = req.ReqDetailEditorTimestamp,
                                        version = req.ReqDetailEditorVersion,
                                        blocks = (req.ReqText != null) ? JsonConvert.DeserializeObject<List<EditorBlock>>(req.ReqText) : null

                                    }
                                });

                                reqDetailsObjList.Add(new RequirementDetailsObject
                                {
                                    ReqSectionId = req.ReqSectionId,
                                    ReqDetailId = req.ReqDetailId,
                                    ReqText = req.ReqText,
                                    ReqDetailEditorTimestamp = req.ReqDetailEditorTimestamp,
                                    ReqDetailEditorVersion
                                    = req.ReqDetailEditorVersion,
                                    ReqNumber = req.ReqNumber,
                                    ParentReqDetailId = req.ParentReqDetailId,
                                    CreateDate = req.CreateDate,
                                    CreatedBy = req.CreatedBy,
                                    StatusChangeDate = req.StatusChangeDate,
                                    Status = req.Status,
                                    ReqContainerId = req.ReqContainerId,
                                    ReqParentContainerId = req.ReqParentContainerId,


                                });

                            }
                        }










                        reqSectionDetailsObj = new RequirementSectionObject
                        {

                            ReqSectionId = requirementSectionData.ReqSectionId,
                            ReqHeaderId = requirementSectionData.ReqHeaderId,
                            ParentSectionId = requirementSectionData.ParentSectionId,
                            SectionLabel = requirementSectionData.SectionLabel,
                            SectionDescription = requirementSectionData.SectionDescription,
                            Version = requirementSectionData.Version,
                            RequirementNumber = requirementSectionData.RequirementNumber,
                            CreatedBy = requirementSectionData.CreatedBy,
                            CreateDate = requirementSectionData.CreateDate,
                            Status = requirementSectionData.Status,
                            StatusChangeDate = requirementSectionData.StatusChangeDate,
                            ReqObjList = reqDetailsObjList,
                            EditorDetailsList = editorDetailsObj

                        };



                    }




                }


                //return Json(new { reqString = reqSectionDetailsObj, statusCode = 200, /*apiStatusCode = ASM_Status_Codes.Maintenance_Execution_Records_Found, apiStatusMessage = "Maintenance Execution Records Found",*/  });




            }
            catch (Exception ex)
            {

                string actionName = this.ControllerContext.RouteData.Values["action"].ToString();
                string controllerName = this.ControllerContext.RouteData.Values["controller"].ToString();
                _logger.LogError(ex, $"Path: {controllerName + "/" + actionName}\n" + ex.Message);
                //return StatusCode(500, ex);
                //return Json(new { statusCode = 500/*, apiStatusCode = "", apiStatusMessage = "Transaction Failed"*/ });

            }

            return reqSectionDetailsObj;

        }

    }
}

