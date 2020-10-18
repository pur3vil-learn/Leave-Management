﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Leave_Management.Contracts;
using Leave_Management.Data;
using Leave_Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Leave_Management.Controllers
{
    [Authorize]
    public class LeaveRequestsController : Controller
    {
        private readonly ILeaveRequestRepository _leaveRequestRepository;
        private readonly ILeaveTypeRepository _leaveTypeRepository;
        private readonly ILeaveAllocationRepository _leaveAllocationRepository;
        private readonly IMapper _mapper;
        private readonly UserManager<Employee> _userManager;

        public LeaveRequestsController(
            ILeaveRequestRepository leaveRequestRepository,
            ILeaveTypeRepository leaveTypeRepository,
            ILeaveAllocationRepository leaveAllocationRepository,
            IMapper mapper,
            UserManager<Employee> userManager)
        {
            _leaveRequestRepository = leaveRequestRepository;
            _leaveTypeRepository = leaveTypeRepository;
            _leaveAllocationRepository = leaveAllocationRepository;
            _mapper = mapper;
            _userManager = userManager;
        }

        [Authorize(Roles = "Administrator")]
        // GET: LeaveRequestController
        public ActionResult Index()
        {
            var leaveRequests = _leaveRequestRepository.FindAll();
            var leaveRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(leaveRequests);
            var model = new AdminLeaveRequestViewViewModel
            {
                TotalRequests = leaveRequestsModel.Count,
                ApprovedRequests = leaveRequestsModel.Where(q => q.Approved == true).Count(),
                PendingRequests = leaveRequests.Where(q => q.Approved == null).Count(),
                RejectedRequests = leaveRequests.Where(q => q.Approved == false).Count(),
                LeaveRequests = leaveRequestsModel
            };
            return View(model);
        }

        // GET: LeaveRequestController/Details/5
        public ActionResult Details(int id)
        {
            var leaveRequest = _leaveRequestRepository.FindById(id);
            var model = _mapper.Map<LeaveRequestViewModel>(leaveRequest);

            return View(model);
        }

        public ActionResult ApproveRequest(int id)
        {
            try
            {
                var user = _userManager.GetUserAsync(User).Result;
                var leaveRequest = _leaveRequestRepository.FindById(id);
                var allocation = _leaveAllocationRepository.GetLeaveAllocationsByEmployeeAndType(leaveRequest.RequestingEmployeeId, leaveRequest.LeaveTypeId);
                int daysRequested = (int)(leaveRequest.EndDate - leaveRequest.StartDate).TotalDays + 1;
                allocation.NumberOfDays -= daysRequested;
                leaveRequest.Approved = true;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepository.Update(leaveRequest);
                _leaveAllocationRepository.Update(allocation);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }
        }

        public ActionResult RejectRequest(int id) 
        {
            try
            {
                var user = _userManager.GetUserAsync(User).Result;
                var leaveRequest = _leaveRequestRepository.FindById(id);
                leaveRequest.Approved = false;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepository.Update(leaveRequest);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: LeaveRequestController/Create
        public ActionResult Create()
        {
            var leaveTypes = _leaveTypeRepository.FindAll();
            var leaveTypeItems = leaveTypes.Select(q => new SelectListItem
            {
                Text = q.Name,
                Value = q.Id.ToString()
            });
            var model = new CreateLeaveRequestViewModel
            {
                LeaveTypes = leaveTypeItems
            };
            return View(model);
        }

        // POST: LeaveRequestController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CreateLeaveRequestViewModel model)
        {
            try
            {
                var startDate = Convert.ToDateTime(model.StartDate);
                var endDate = Convert.ToDateTime(model.EndDate);

                var leaveTypes = _leaveTypeRepository.FindAll();
                var leaveTypeItems = leaveTypes.Select(q => new SelectListItem
                {
                    Text = q.Name,
                    Value = q.Id.ToString()
                });
                model.LeaveTypes = leaveTypeItems;
                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                if (DateTime.Compare(startDate, endDate) > 0)
                {
                    ModelState.AddModelError("", "Start Date must be prior to the End Date");
                    return View(model);
                }
                var employee = _userManager.GetUserAsync(User).Result;
                var allocation = _leaveAllocationRepository.GetLeaveAllocationsByEmployeeAndType(employee.Id, model.LeaveTypeId);
                int daysRequested = (int) (endDate.Date - startDate.Date).TotalDays + 1;
                
                if(daysRequested > allocation.NumberOfDays)
                {
                    ModelState.AddModelError("", "You do not have sufficient days for this request");
                    return View(model);
                }

                var leaveRequestModel = new LeaveRequestViewModel
                {
                    RequestingEmployeeId = employee.Id,
                    LeaveTypeId = model.LeaveTypeId,
                    StartDate = startDate,
                    EndDate = endDate,
                    DateRequested = DateTime.Now,
                    Approved = null,
                    DateActioned = null,
                    RequestComment = model.RequestComment
                };
                var leaveRequest = _mapper.Map<LeaveRequest>(leaveRequestModel);
                var isSuccess = _leaveRequestRepository.Create(leaveRequest);
                if (!isSuccess)
                {
                    ModelState.AddModelError("", "Something went wrong submitting your record");
                    return View(model);
                }
                return RedirectToAction(nameof(MyLeave));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Something went wrong");
                return View(model);
            }
        }

        public ActionResult MyLeave()
        {
            var employee = _userManager.GetUserAsync(User).Result;
            var allocations = _leaveAllocationRepository.GetLeaveAllocationsByEmployee(employee.Id);
            var leaveAllocationsModel = _mapper.Map<List<LeaveAllocationViewModel>>(allocations);
            var leaveRequests = _leaveRequestRepository.GetLeaveRequestsByEmployee(employee.Id);
            var leaveRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(leaveRequests);
            var model = new EmployeeLeaveRequestViewViewModel
            {
                LeaveAllocations = leaveAllocationsModel,
                LeaveRequests = leaveRequestsModel
            };
            return View(model);
        }

        public ActionResult CancelRequest(int id)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return RedirectToAction(nameof(MyLeave));
                }
                var request = _leaveRequestRepository.FindById(id);
                if(request.Approved == true)
                {
                    // add the days back to the allocation
                    var allocation = _leaveAllocationRepository.GetLeaveAllocationsByEmployeeAndType(request.RequestingEmployeeId, request.LeaveTypeId);
                    int daysRequested = (int)(request.EndDate.Date - request.StartDate.Date).TotalDays + 1;
                    allocation.NumberOfDays += daysRequested;
                    _leaveAllocationRepository.Update(allocation);
                }
                request.Cancelled = true;
                _leaveRequestRepository.Update(request);
                return RedirectToAction(nameof(MyLeave));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("","Something went wrong");
                return RedirectToAction(nameof(MyLeave));
            }
        }

        // GET: LeaveRequestController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: LeaveRequestController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: LeaveRequestController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: LeaveRequestController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}