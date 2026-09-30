const fs = require('fs');
const content = fs.readFileSync('c:\\Users\\אור\\Desktop\\Third Year Coding!\\פיתוח אפליקציות\\React Final Project\\ReactFinalProject\\ReactProject\\React-Native-Final-Project\\Instructor\\InstructorProgressReport.js', 'utf8');

const lines = content.split('\n');
lines.forEach((line, index) => {
  if (line.includes('toIsoDateString')) {
    console.log(`${index + 1}: ${line.trim()}`);
  }
});
