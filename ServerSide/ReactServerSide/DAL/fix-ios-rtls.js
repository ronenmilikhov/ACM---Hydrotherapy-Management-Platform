const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
let content = fs.readFileSync(uiPath, 'utf8');

// Update pickerContainer styles
const oldPickerContainer = `  pickerContainer: {
    flexDirection: Platform.OS === 'ios' ? 'row-reverse' : 'row',
    flexWrap: 'wrap',
    gap: 8,
    justifyContent: Platform.OS === 'ios' ? 'flex-end' : 'flex-start',
    marginBottom: 15,
  },`;

const newPickerContainer = `  pickerContainer: {
    flexDirection: isRTL ? 'row' : 'row-reverse',
    flexWrap: 'wrap',
    gap: 8,
    justifyContent: isRTL ? 'flex-start' : 'flex-end',
    marginBottom: 15,
  },`;

// Update removedMetricsList styles
const oldRemovedMetricsList = `  removedMetricsList: {
    flexDirection: Platform.OS === 'ios' ? 'row-reverse' : 'row',
    flexWrap: 'wrap',
    gap: 6,
    justifyContent: Platform.OS === 'ios' ? 'flex-end' : 'flex-start',
    marginTop: 6,
  },`;

const newRemovedMetricsList = `  removedMetricsList: {
    flexDirection: isRTL ? 'row' : 'row-reverse',
    flexWrap: 'wrap',
    gap: 6,
    justifyContent: isRTL ? 'flex-start' : 'flex-end',
    marginTop: 6,
  },`;

if (content.includes(oldPickerContainer)) {
  content = content.replace(oldPickerContainer, newPickerContainer);
  console.log("Successfully replaced pickerContainer styling.");
} else {
  console.log("oldPickerContainer styling not found directly. Let's try flexible replace.");
  // Fallback regex or substring search
  content = content.replace(
    /flexDirection:\s*Platform\.OS\s*===\s*'ios'\s*\?\s*'row-reverse'\s*:\s*'row',(\s*flexWrap:\s*'wrap',)?(\s*gap:\s*8,)?\s*justifyContent:\s*Platform\.OS\s*===\s*'ios'\s*\?\s*'flex-end'\s*:\s*'flex-start',/g,
    "flexDirection: isRTL ? 'row' : 'row-reverse',\n    flexWrap: 'wrap',\n    gap: 8,\n    justifyContent: isRTL ? 'flex-start' : 'flex-end',"
  );
}

if (content.includes(oldRemovedMetricsList)) {
  content = content.replace(oldRemovedMetricsList, newRemovedMetricsList);
  console.log("Successfully replaced removedMetricsList styling.");
} else {
  console.log("oldRemovedMetricsList styling not found directly. Let's try flexible replace.");
  content = content.replace(
    /flexDirection:\s*Platform\.OS\s*===\s*'ios'\s*\?\s*'row-reverse'\s*:\s*'row',(\s*flexWrap:\s*'wrap',)?(\s*gap:\s*6,)?\s*justifyContent:\s*Platform\.OS\s*===\s*'ios'\s*\?\s*'flex-end'\s*:\s*'flex-start',/g,
    "flexDirection: isRTL ? 'row' : 'row-reverse',\n    flexWrap: 'wrap',\n    gap: 6,\n    justifyContent: isRTL ? 'flex-start' : 'flex-end',"
  );
}

fs.writeFileSync(uiPath, content, 'utf8');
console.log("File write complete.");
