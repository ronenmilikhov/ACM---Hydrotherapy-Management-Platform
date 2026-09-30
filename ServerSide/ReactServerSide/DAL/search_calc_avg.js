const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'Controllers', 'ChatController.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

let startLine = -1;
lines.forEach((line, idx) => {
  if (line.includes('CalculateAverageScoreFromReports')) {
    startLine = idx;
  }
});

if (startLine !== -1) {
  console.log(`Found CalculateAverageScoreFromReports at line ${startLine + 1}`);
  for (let i = Math.max(0, startLine - 2); i < Math.min(lines.length, startLine + 80); i++) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
} else {
  console.log("CalculateAverageScoreFromReports not found");
}
