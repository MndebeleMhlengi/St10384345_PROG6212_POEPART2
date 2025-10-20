# St10384345_PROG6212_POEPART2
Contract Monthly Claim System (CMCS)
Project Overview
The Contract Monthly Claim System (CMCS) is a comprehensive .NET web-based application designed to streamline the process of submitting and approving monthly claims for Independent Contractor (IC) lecturers. The system provides a multi-level approval workflow with role-based access control.

Features
User Roles & Capabilities
Lecturers: Submit claims, upload supporting documents, track claim status
Programme Coordinators: Review and approve/reject claims from lecturers
Academic Managers: Final approval of claims before HR processing
HR: Process payments, manage lecturer data, generate reports

Core Functionality
✅ Multi-level claim approval workflow
✅ Secure document upload and management
✅ Real-time claim status tracking
✅ Automated total amount calculation
✅ Comprehensive validation and error handling
✅ Role-based dashboard views
✅ Responsive user interface

Technology Stack
Backend
Framework: ASP.NET Core MVC
Database: SQL Server with Entity Framework Core
Authentication: Cookie-based authentication with claims
Logging: Structured logging with ILogger

Frontend
UI Framework: Bootstrap 5
JavaScript: jQuery with DataTables
Icons: Font Awesome
Validation: Client-side and server-side validation

Testing
Framework: xUnit
Coverage: Comprehensive unit and integration tests

Project Structure
text
CMCS/
├── Controllers/
│   ├── AccountController.cs
│   ├── LecturerController.cs
│   ├── ProgrammeCoordinatorController.cs
│   ├── AcademicManagerController.cs
│   └── HRController.cs
├── Models/
│   ├── User.cs
│   ├── Claim.cs
│   ├── Document.cs
│   ├── ClaimApproval.cs
│   └── ViewModels/
├── Data/
│   └── ApplicationDbContext.cs
├── Views/
├── wwwroot/
└── Tests/

Setup Instructions
Prerequisites
.NET 6.0 SDK or later
SQL Server (LocalDB or Express)
Visual Studio 2022 or VS Code

Installation Steps
Clone the repository
Update connection string in appsettings.json
Run database migrations: dotnet ef database update
Build the solution: dotnet build
Run the application: dotnet run

Default Roles
The system supports four main roles:
LECTURER
PROGRAMME_COORDINATOR
ACADEMIC_MANAGER
HR

Testing
Run the test suite with:
bash
dotnet test

Security Features
Password hashing with HMACSHA512
Role-based authorization
Anti-forgery token validation
Secure file upload restrictions
Session management with sliding expiration
