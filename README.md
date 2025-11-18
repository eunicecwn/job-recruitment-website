# HireRightPro - Job Recruitment Website

## 📑 Table of Contents

* [Project Overview](#-project-overview)
* [Technology Stack](#-technology-stack)
* [Environment Setup](#-environment-setup)
* [Features](#-features)
* [User Roles](#-user-roles)
* [Database Setup](#-database-setup)
* [Running the Application](#-running-the-application)
* [Project Structure](#-project-structure)
* [Security Features](#-security-features)
* [Future Improvements](#-future-improvements)
* [License](#-license)
* [Authors](#-authors)

---

## 🎯 Project Overview

**HireRightPro** is an ASP.NET MVC job recruitment platform that facilitates seamless connections between job seekers and employers. The system features an advanced role-based architecture with dedicated interfaces for different user types, providing tailored experiences for job seekers, employers, and administrators.

---

## 🛠️ Technology Stack

### Backend
* **C# ASP.NET MVC** - Model-View-Controller architecture
* **Entity Framework** - Database management and ORM
* **SQL Server** - Data persistence and management
* **SignalR** - Real-time notifications

### Frontend
* **Bootstrap 5** - Responsive UI framework
* **JavaScript ES6+** - Client-side functionality
* **CSS3** - Custom styling with CSS variables
* **jQuery** - DOM manipulation and AJAX

### Additional Technologies
* **Chart.js** - Data visualization for analytics
* **Leaflet.js** - Interactive maps for location selection
* **Cropper.js** - Image cropping for profile photos
* **PDF.js** - In-browser resume preview
* **SortableJS** - Drag-and-drop functionality
* **Font Awesome & Bootstrap Icons** - Comprehensive icon library

---

## ⚙️ Environment Setup

### Prerequisites

Make sure the following are installed:

* .NET 6.0 or higher
* SQL Server 2019 or higher
* Visual Studio 2022 or compatible IDE
* Modern web browser with JavaScript enabled

### Installation Steps

1. Clone or download the project
2. Update connection string in `appsettings.json`
3. Run Entity Framework migrations
4. Seed initial data (categories, admin user)

```bash
git clone https://github.com/your-username/hirerightpro.git
cd hirerightpro
dotnet build
dotnet run
```

---

## ✨ Features

### 👥 Multi-Role User System
* **Job Seeker Portal** - Profile management, job search, application tracking
* **Employer Dashboard** - Company profiles, job posting, application management
* **Admin Panel** - User management, report monitoring, system administration

### 💼 Job Management
* **Advanced Job Search** - Filter by location, salary, category, job type
* **Job Posting System** - Rich job descriptions with requirements
* **Smart Application Process** - Resume upload and cover letter submission

### 📊 Application System
* **Real-time Status Updates** - Track application progress
* **Interview Management** - Calendar integration for scheduling
* **Question Sets** - Customizable pre-application questionnaires

### 🤖 AI-Powered Features
* **AI Assistant** - 24/7 chatbot for platform guidance
* **Smart Recommendations** - Job matching based on profile
* **Automated Notifications** - Real-time updates

### ⚖️ Administrative Features
* **User Management** - Account locking and administrative controls
* **Reporting System** - Job and user reporting for community safety
* **Analytics Dashboard** - Comprehensive platform insights

---

## 👤 User Roles

### Job Seekers
1. **Registration & Profile** - Create account and complete professional profile
2. **Job Search** - Use advanced filters to find relevant positions
3. **Applications** - Apply to jobs with resume and cover letter
4. **Tracking** - Monitor application status and receive notifications

### Employers
1. **Company Profile** - Set up company information and branding
2. **Job Posting** - Create detailed job listings with requirements
3. **Application Management** - Review and process candidate applications
4. **Interview Scheduling** - Coordinate interviews through the platform

### Administrators
1. **Dashboard** - System overview and analytics
2. **User Management** - Monitor and manage user accounts
3. **Report Handling** - Review reported jobs and users
4. **System Monitoring** - Track platform performance

---

## 🗄️ Database Setup

The system uses **SQL Server** with Entity Framework for data management.

* To initialize the database, run Entity Framework migrations
* Seed initial data including job categories and admin user
* Connection string configuration in `appsettings.json`

---

## 🚀 Running the Application

After setup, start the application with:

```bash
dotnet run
```

Access the application at `https://localhost:7000` (or your configured port)

---

## 🏗️ Project Structure

```
HireRightPro/
├── Controllers/          # MVC Controllers
├── Models/              # Data Models and View Models
├── Views/               # Razor Views and Partial Views
├── wwwroot/            # Static Assets (CSS, JS, Images)
├── Services/           # Business Logic Layer
├── Data/              # Entity Framework Context
└── ViewModels/        # View-Specific Models
```

---

## 🔒 Security Features

### Authentication & Authorization
* **ASP.NET Identity** - Robust user management system
* **Role-based Access Control** - Granular permissions for different user types
* **Anti-Forgery Tokens** - CSRF protection on all forms

### Data Protection
* **Input Sanitization** - XSS prevention through proper encoding
* **SQL Injection Prevention** - Parameterized queries with Entity Framework
* **File Upload Security** - Type validation and size restrictions

### Advanced Security
* **Email OTP Verification** - Two-step email verification
* **Password Strength Validation** - Real-time password requirements
* **reCAPTCHA Integration** - Bot prevention and security enhancement

---

## 🚀 Future Improvements

* **Mobile Application** - Native iOS and Android apps
* **Advanced Analytics** - Machine learning for job matching
* **Video Interview Integration** - Built-in video calling features
* **Multi-language Support** - Internationalization for global reach
* **Enhanced AI Features** - Advanced chatbot and recommendation engine

---

## 📄 License

This project is created for **academic purposes**.  
Not intended for commercial distribution.

---

## 👥 Authors

* Developed as a group project for ASP.NET MVC coursework
* Using modern web development practices and enterprise-grade architecture

---

## 🔧 Key Technical Components

### Layout System
* **Main Layout** - Job seeker interface with sidebar navigation
* **Employer Layout** - Dedicated employer dashboard interface
* **Admin Layout** - Administrative control panel
* **Responsive Design** - Mobile-first approach

### Advanced Features
* **Question Set Management** - Drag-and-drop interface for custom questionnaires
* **Real-time Updates** - Live notification system with SignalR
* **Payment Integration** - Stripe integration for premium features
* **Calendar System** - Multi-view calendar for interview scheduling

### Performance Optimizations
* **AJAX Implementation** - Real-time search and filtering
* **Pagination System** - Efficient data loading for large datasets
* **Caching Strategy** - Performance optimization for frequently accessed data
* **Lazy Loading** - On-demand content loading
