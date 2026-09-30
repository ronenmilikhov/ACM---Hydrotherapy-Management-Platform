# 🌊 ACM Application
> **React Native Mobile App & .NET 8 Web API Server**  
> Developed for **Ruppin Group 25-18**

![React Native](https://img.shields.io/badge/React_Native-0.81.5-61DAFB?style=for-the-badge&logo=react&logoColor=black)
![Expo](https://img.shields.io/badge/Expo-v54.0-000000?style=for-the-badge&logo=expo&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![JavaScript](https://img.shields.io/badge/JavaScript-ES6+-F7DF1E?style=for-the-badge&logo=javascript&logoColor=black)
![C#](https://img.shields.io/badge/C%23-12.0-239120?style=for-the-badge&logo=c-sharp&logoColor=white)

---

## 📖 Overview

**ACM Application** is a full-stack hydrotherapy management platform for coordinating lessons, child development, and communication between instructors, parents, and managers. The system combines a cross-platform mobile application with a secure REST API and AWS-backed data services.

### Key Roles & Features
- 👨‍💼 **Manager Portal**: Group management, system reports, instructor overview, and attendance tracking.
- 🏊‍♂️ **Instructor Portal**: Lesson scheduling, attendance boards, achievement editing, progress reporting, and student profiles.
- 👨‍👩‍👧 **Parent Portal**: Progress reports, lesson history, appointment booking, and instant notifications.
- 🤖 **AI-Powered Insights**: Automated progress summaries, group analysis, and exercise recommendations based on development data.

### Architecture

The platform uses a two-tier architecture:

- **Mobile client**: A React Native and Expo application with role-based navigation for managers, instructors, and parents.
- **REST API**: An ASP.NET Core 8 Web API written in C# that handles authentication, authorization, validation, scheduling, reporting, chat, and notifications.
- **Data services**: An application data layer that integrates Amazon DynamoDB and Amazon RDS for user, child, group, lesson, attendance, and progress data.
- **AWS deployment**: The backend and supporting infrastructure were deployed using Amazon Web Services.
- **External integrations**: Push notifications, file attachments, and an AI provider are accessed through backend services so the mobile client remains focused on the user experience.

The main request flow is:

```text
React Native App -> ASP.NET Core REST API -> DBServices -> Amazon DynamoDB / Amazon RDS
```

---

## 🛠 Tech Stack

- **Frontend**: React Native, Expo, React Navigation, Vector Icons, Linear Gradient, SVG
- **Backend**: C# ASP.NET Core 8 Web API
- **Cloud & Data**: Amazon Web Services, Amazon DynamoDB, Amazon RDS
- **Security**: JWT bearer authentication, role-based authorization, and PBKDF2 password hashing
- **Capabilities**: Scheduling, attendance management, progress reporting, chat, push notifications, and AI-assisted analysis
- **Testing**: Expo Go (iOS & Android)

### Professional Skills Demonstrated

- Full-stack mobile and backend development
- React Native, Expo, JavaScript, and React Navigation
- C#, ASP.NET Core, REST API design, and Swagger/OpenAPI
- JWT authentication and role-based access control
- Amazon DynamoDB and Amazon RDS data integration
- Data validation, CRUD workflows, and business-rule implementation
- Lesson scheduling and availability conflict detection
- Attendance, progress tracking, and report generation
- Push notifications and in-app messaging
- AI API integration and structured JSON processing
- AWS deployment and cloud-based application architecture
- Hebrew localization and right-to-left mobile UI development

---

## 📋 Prerequisites

Before running the application, make sure you have the following installed on your machine:

1. **Node.js** (v18.0.0 or higher) & **npm**  
   👉 [Download Node.js](https://nodejs.org/)
2. **.NET 8 SDK** (Required if running the backend server locally)  
   👉 [Download .NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
3. **Expo Go App** on your mobile phone:
   - 📱 **iOS**: Download from [Apple App Store](https://apps.apple.com/app/expo-go/id982107779)
   - 🤖 **Android**: Download from [Google Play Store](https://play.google.com/store/apps/details?id=host.exp.exponent)
   - *Ensure your phone and computer are connected to the same Wi-Fi network.*

---

## 🚀 Step-by-Step Setup & Execution

### 1️⃣ Clone the Repository

Open your terminal or command prompt and run:
```bash
git clone https://github.com/rupcgroup25-18/ACM-App.git
cd ACM-App
```

---

### 2️⃣ Install Dependencies

Install the required npm packages for the React Native app:
```bash
npm install
```

---

### 3️⃣ Configure Local Environment Variables

#### Frontend Configuration (`.env`)
Create a `.env` file in the root folder by copying `.env.example`:
```bash
# On Windows PowerShell:
copy .env.example .env

# On Mac/Linux:
cp .env.example .env
```
Default `.env` contents:
```env
EXPO_PUBLIC_API_BASE_URL=https://your-aws-api-domain.example.com/api
```

#### Backend Configuration (`appsettings.json`)
If running the backend server locally, navigate to `ServerSide/ReactServerSide/` and copy `appsettings.Example.json` to `appsettings.json`:
```bash
cd ServerSide/ReactServerSide
copy appsettings.Example.json appsettings.json
cd ../..
```

---

### 4️⃣ Start the Backend Server (Optional / Local Dev)

To launch the .NET Web API backend server locally:
```bash
cd ServerSide/ReactServerSide
dotnet run
```
The server will start listening for API endpoints at `https://localhost:7043` or `http://localhost:5043`.

---

### 5️⃣ Run the Mobile App & Test in Expo Go 📱

Navigate back to the root directory and start Expo:
```bash
npm start
```
*Alternatively, you can run:*
```bash
npx expo start
```

#### 📲 Testing on your physical phone with **Expo Go**:
1. Open the **Expo Go** app on your iOS or Android device.
2. **On Android**: Tap "Scan QR code" and point your camera at the QR code displayed in your terminal.
3. **On iOS**: Open your built-in iPhone Camera app and scan the QR code displayed in your terminal, then tap the link to open in Expo Go.
4. The application will bundle and launch instantly on your device! 🎉

#### 🖥️ Testing on Simulators / Web:
While the terminal process is running, press:
- `a` to open in **Android Emulator**
- `i` to open in **iOS Simulator**
- `w` to open in **Web Browser**

---

## 📂 Project Architecture

```
ACM-App/
├── assets/                     # App icons, splash screens, and images
├── components/                 # Reusable UI components & backgrounds
├── Instructor/                 # Instructor portal screens & logic
├── Manager/                    # Manager portal screens & logic
├── Parent/                     # Parent portal screens & logic
├── notifications/              # Push notification handlers
├── theme/                      # App design tokens & global styles
├── ServerSide/                 # .NET 8 Web API backend server
│   └── ReactServerSide/        # ASP.NET Core project files & DAL
├── App.js                      # Main App Entry & Navigation container
├── apiConfig.js                # API base URL resolver & Auth headers
├── .env.example                # Frontend environment template
├── .gitignore                  # Git security & binary exclusions
└── package.json                # Project dependencies & scripts
```

---

## 🤝 Support & Contribution

For questions or issues related to this project, please open an issue in the repository or contact the **Ruppin Group 25-18** team.