const fs = require('fs');
const path = require('path');

function walkDir(dir, callback) {
  fs.readdirSync(dir).forEach(f => {
    let dirPath = path.join(dir, f);
    let isDirectory = fs.statSync(dirPath).isDirectory();
    if (isDirectory) {
      if (f !== 'node_modules' && f !== '.expo' && f !== '.git' && f !== 'bin' && f !== 'obj' && f !== 'dist') {
        walkDir(dirPath, callback);
      }
    } else {
      callback(dirPath);
    }
  });
}

function scan() {
  const root = path.join(__dirname, '..', '..', '..');
  walkDir(root, (filePath) => {
    if (filePath.endsWith('.js') || filePath.endsWith('.tsx') || filePath.endsWith('.ts')) {
      const content = fs.readFileSync(filePath, 'utf8');
      
      // Match the entire import block from react-native
      // e.g. import { ... } from 'react-native'; or "react-native"
      const match = content.match(/import\s*\{([\s\S]*?)\}\s*from\s*['"]react-native['"]/);
      
      if (/\bPlatform\b/.test(content)) {
        if (!match) {
          // Check if it is imported as default or not at all
          const defaultMatch = content.match(/import\s+(\w+)\s+from\s*['"]react-native['"]/);
          if (!defaultMatch) {
            console.log(`>>> ERROR: File ${filePath} uses 'Platform' but has NO react-native import statement!`);
          } else {
            console.log(`File: ${filePath} uses default import: ${defaultMatch[0].trim()}`);
          }
        } else {
          const importedItems = match[1].split(',').map(item => item.trim());
          if (!importedItems.includes('Platform')) {
            console.log(`>>> ERROR: File ${filePath} uses 'Platform' but 'Platform' is NOT in the import list!`);
            console.log(`  Import list: ${match[0].replace(/\s+/g, ' ').trim()}`);
          }
        }
      }
    }
  });
}

scan();
