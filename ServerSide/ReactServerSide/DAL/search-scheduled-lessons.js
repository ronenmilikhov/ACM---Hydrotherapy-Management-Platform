const fs = require('fs');
const path = require('path');

const dir = 'c:\\Users\\אור\\Desktop\\Third Year Coding!\\פיתוח אפליקציות\\React Final Project\\ReactFinalProject\\ReactProject\\React-Native-Final-Project\\Instructor';
const files = fs.readdirSync(dir);

files.forEach(file => {
  if (file.endsWith('.js')) {
    const content = fs.readFileSync(path.join(dir, file), 'utf8');
    const lines = content.split('\n');
    lines.forEach((line, index) => {
      if (line.includes('שנקבעו') || line.includes('upcoming') || line.includes('Upcoming') || line.includes('LessonInvitations')) {
        console.log(`${file}:${index + 1}: ${line.trim()}`);
      }
    });
  }
});
