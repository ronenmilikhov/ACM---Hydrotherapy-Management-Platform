import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Keyboard,
  Modal,
  Platform,
  SafeAreaView,
  ScrollView,
  StatusBar,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import styles from './ManagerHomepage.styles';
import PrimaryButton from '../components/ui/PrimaryButton';
import LoginAquaticBackground from '../components/ui/LoginAquaticBackground';
import { API_BASE_URL, buildAuthHeaders, buildJsonAuthHeaders } from '../apiConfig';

const SWIMMING_SKILL_AREAS = [
  { key: 'water_confidence', label: 'הסתגלות וביטחון במים' },
  { key: 'breathing', label: 'שליטה בנשימות' },
  { key: 'coordination', label: 'תנועתיות וקואורדינציה' },
  { key: 'floating', label: 'יציבה וציפה' },
  { key: 'communication', label: 'תקשורת במים ושיתוף פעולה' },
  { key: 'persistence', label: 'התמדה ומאמץ' },
  { key: 'initiative', label: 'יוזמה' },
  { key: 'focus', label: 'קשב וריכוז' },
  { key: 'instructions', label: 'תגובה להוראות' },
  { key: 'independence', label: 'עצמאות בתרגיל' },
];

const StatCard = ({ number, label, variant }) => (
  <View style={[styles.statCard, variant === 'warm' ? styles.statCardWarm : styles.statCardCool]}>
    <Text style={[styles.statNumber, variant === 'warm' ? styles.statNumberWarm : styles.statNumberCool]}>
      {number}
    </Text>
    <Text style={styles.statLabel}>{label}</Text>
  </View>
);

const ManagerActionIcon = ({ icon, label, onPress, tintStyle, offsetStyle, disabled }) => (
  <TouchableOpacity
    activeOpacity={0.88}
    style={[
      styles.actionIconTile,
      tintStyle,
      offsetStyle,
      disabled ? styles.actionIconTileDisabled : null,
    ]}
    onPress={onPress}
    disabled={disabled}
  >
    <View style={[styles.actionIconOrb, disabled ? styles.actionIconOrbDisabled : null]}>
      <Text style={styles.actionIconGlyph}>{icon}</Text>
    </View>
    <Text style={[styles.actionIconLabel, disabled ? styles.actionIconLabelDisabled : null]}>{label}</Text>
  </TouchableOpacity>
);

export default function ManagerHomepage({ navigation, route }) {
  const [showCreateGroupModal, setShowCreateGroupModal] = useState(false);
  const [showCreateInstructorModal, setShowCreateInstructorModal] = useState(false);
  const [showCreateParentModal, setShowCreateParentModal] = useState(false);
  const [showCreateChildModal, setShowCreateChildModal] = useState(false);
  const [showEditInstructorModal, setShowEditInstructorModal] = useState(false);
  const [showEditParentModal, setShowEditParentModal] = useState(false);
  const [showEditChildModal, setShowEditChildModal] = useState(false);
  const [groups, setGroups] = useState([]);
  const [instructors, setInstructors] = useState([]);
  const [parents, setParents] = useState([]);
  const [children, setChildren] = useState([]);
  const [isLoadingData, setIsLoadingData] = useState(false);
  const [isSavingGroup, setIsSavingGroup] = useState(false);
  const [isSavingInstructor, setIsSavingInstructor] = useState(false);
  const [isSavingParent, setIsSavingParent] = useState(false);
  const [isSavingChild, setIsSavingChild] = useState(false);
  const [isUpdatingInstructor, setIsUpdatingInstructor] = useState(false);
  const [isUpdatingParent, setIsUpdatingParent] = useState(false);
  const [isUpdatingChild, setIsUpdatingChild] = useState(false);
  const [searchInstructorText, setSearchInstructorText] = useState('');
  const [searchParentText, setSearchParentText] = useState('');
  const [searchChildText, setSearchChildText] = useState('');
  const [searchGroupText, setSearchGroupText] = useState('');
  const [expandedGroupChildrenId, setExpandedGroupChildrenId] = useState(null);
  const [newGroupName, setNewGroupName] = useState('');
  const [newGroupDescription, setNewGroupDescription] = useState('');
  const [newInstructorFirstName, setNewInstructorFirstName] = useState('');
  const [newInstructorLastName, setNewInstructorLastName] = useState('');
  const [newInstructorEmail, setNewInstructorEmail] = useState('');
  const [newInstructorPassword, setNewInstructorPassword] = useState('');
  const [newParentFirstName, setNewParentFirstName] = useState('');
  const [newParentLastName, setNewParentLastName] = useState('');
  const [newParentEmail, setNewParentEmail] = useState('');
  const [newParentPassword, setNewParentPassword] = useState('');
  const [newParentPhone, setNewParentPhone] = useState('');
  const [newChildFirstName, setNewChildFirstName] = useState('');
  const [newChildLastName, setNewChildLastName] = useState('');
  const [newChildBirthDate, setNewChildBirthDate] = useState('');
  const [newChildDescription, setNewChildDescription] = useState('');
  const [newChildParentId, setNewChildParentId] = useState(null);
  const [newChildStrengths, setNewChildStrengths] = useState([]);
  const [newChildWeaknesses, setNewChildWeaknesses] = useState([]);
  const [editInstructorId, setEditInstructorId] = useState(null);
  const [editInstructorFirstName, setEditInstructorFirstName] = useState('');
  const [editInstructorLastName, setEditInstructorLastName] = useState('');
  const [editInstructorEmail, setEditInstructorEmail] = useState('');
  const [editParentId, setEditParentId] = useState(null);
  const [editParentFirstName, setEditParentFirstName] = useState('');
  const [editParentLastName, setEditParentLastName] = useState('');
  const [editParentEmail, setEditParentEmail] = useState('');
  const [editChildId, setEditChildId] = useState(null);
  const [editChildFirstName, setEditChildFirstName] = useState('');
  const [editChildLastName, setEditChildLastName] = useState('');
  const [editChildBirthDate, setEditChildBirthDate] = useState('');
  const [editChildDescription, setEditChildDescription] = useState('');
  const [editChildParentId, setEditChildParentId] = useState(null);
  const [editChildStrengths, setEditChildStrengths] = useState([]);
  const [editChildWeaknesses, setEditChildWeaknesses] = useState([]);
  const [editChildGroupId, setEditChildGroupId] = useState(null);
  const [originalEditChildGroupId, setOriginalEditChildGroupId] = useState(null);
  const [originalEditChildGroupName, setOriginalEditChildGroupName] = useState('');
  const [searchEditChildGroupText, setSearchEditChildGroupText] = useState('');
  const [isEditChildGroupDropdownOpen, setIsEditChildGroupDropdownOpen] = useState(false);
  const [editChildAiRecommendation, setEditChildAiRecommendation] = useState(null);
  const [isFindingAiGroupRecommendation, setIsFindingAiGroupRecommendation] = useState(false);
  const [isApplyingAiRecommendedGroup, setIsApplyingAiRecommendedGroup] = useState(false);
  const [showEditGroupModal, setShowEditGroupModal] = useState(false);
  const [editGroupId, setEditGroupId] = useState(null);
  const [editGroupName, setEditGroupName] = useState('');
  const [editGroupDescription, setEditGroupDescription] = useState('');
  const [editGroupInstructorId, setEditGroupInstructorId] = useState(null);
  const [searchEditGroupInstructorText, setSearchEditGroupInstructorText] = useState('');
  const [isEditGroupInstructorDropdownOpen, setIsEditGroupInstructorDropdownOpen] = useState(false);
  const [isUpdatingGroup, setIsUpdatingGroup] = useState(false);
  const [selectedInstructorId, setSelectedInstructorId] = useState(null);
  const [isCreateInstructorDropdownOpen, setIsCreateInstructorDropdownOpen] = useState(false);
  const [isCreateChildParentDropdownOpen, setIsCreateChildParentDropdownOpen] = useState(false);
  const [isEditChildParentDropdownOpen, setIsEditChildParentDropdownOpen] = useState(false);
  const [createGroupErrors, setCreateGroupErrors] = useState({ name: '', description: '', instructor: '' });
  const [createInstructorErrors, setCreateInstructorErrors] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
  });
  const [createParentErrors, setCreateParentErrors] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    phone: '',
  });
  const [createChildErrors, setCreateChildErrors] = useState({
    firstName: '',
    lastName: '',
    birthDate: '',
    childDescription: '',
    parentId: '',
  });
  const [editInstructorErrors, setEditInstructorErrors] = useState({ firstName: '', lastName: '' });
  const [editParentErrors, setEditParentErrors] = useState({ firstName: '', lastName: '' });
  const [editChildErrors, setEditChildErrors] = useState({
    firstName: '',
    lastName: '',
    birthDate: '',
    parentId: '',
  });
  const [editGroupErrors, setEditGroupErrors] = useState({
    name: '',
    description: '',
    instructor: '',
  });

  const managerAuth = route?.params?.managerAuth;
  const authToken = useMemo(() => String(managerAuth?.token || '').trim(), [managerAuth?.token]);
  const hasManagerAuth = authToken.length > 0;

  const selectedInstructor = instructors.find((instructor) => instructor.instructorId === selectedInstructorId) || null;
  const selectedChildParent = parents.find((parent) => parent.parentId === newChildParentId) || null;
  const selectedEditChildParent = parents.find((parent) => parent.parentId === editChildParentId) || null;
  const selectedEditChildGroup = groups.find((group) => group.groupId === editChildGroupId) || null;
  const selectedEditGroupInstructor = instructors.find((instructor) => instructor.instructorId === editGroupInstructorId) || null;
  const originalEditGroup = groups.find((group) => group.groupId === editGroupId) || null;
  const originalEditGroupInstructorId = originalEditGroup?.instructorId || null;
  const originalEditGroupInstructorDisplay = originalEditGroup
    ? (originalEditGroup.instructorEmail && originalEditGroup.instructorFullName
      ? `${originalEditGroup.instructorEmail} - ${originalEditGroup.instructorFullName}`
      : originalEditGroup.instructorEmail || originalEditGroup.instructorFullName || '')
    : '';
  const hasInstructors = instructors.length > 0;
  const hasParents = parents.length > 0;
  const hasGroups = groups.length > 0;

  const ensureManagerAuth = useCallback((purpose) => {
    if (hasManagerAuth) {
      return true;
    }

    Alert.alert('אין הרשאת מנהל', `נדרש להתחבר מחדש כמנהל כדי ${purpose}.`);
    return false;
  }, [hasManagerAuth]);

  const performLogout = useCallback(() => {
    navigation.reset({
      index: 0,
      routes: [{ name: 'Login' }],
    });
  }, [navigation]);

  const handleDisconnect = useCallback(() => {
    if (Platform.OS === 'web') {
      const shouldDisconnect = typeof globalThis.confirm === 'function'
        ? globalThis.confirm('האם אתה בטוח שברצונך להתנתק?')
        : true;

      if (shouldDisconnect) {
        performLogout();
      }

      return;
    }

    Alert.alert(
      'התנתקות',
      'האם אתה בטוח שברצונך להתנתק?',
      [
        { text: 'ביטול', style: 'cancel' },
        {
          text: 'התנתקות',
          style: 'destructive',
          onPress: performLogout,
        },
      ],
      { cancelable: true },
    );
  }, [performLogout]);

  const dismissKeyboardOnTouchCapture = useCallback(() => {
    Keyboard.dismiss();
    return false;
  }, []);

  const parseApiPayload = useCallback(async (response) => {
    if (!response || response.status === 204) {
      return null;
    }

    const text = await response.text().catch(() => '');
    if (!text) {
      return null;
    }

    try {
      return JSON.parse(text);
    } catch (_error) {
      return { message: text };
    }
  }, []);

  const confirmDestructiveAction = useCallback((title, message, onConfirm) => {
    if (Platform.OS === 'web') {
      const confirmed = typeof window !== 'undefined' ? window.confirm(`${title}\n\n${message}`) : false;
      if (confirmed) {
        onConfirm();
      }
      return;
    }

    Alert.alert(
      title,
      message,
      [
        { text: 'ביטול', style: 'cancel' },
        { text: 'מחיקה', style: 'destructive', onPress: onConfirm },
      ]
    );
  }, []);

  const loadGroupsAndInstructors = useCallback(async () => {
    if (!hasManagerAuth) {
      setGroups([]);
      setInstructors([]);
      setParents([]);
      setChildren([]);
      return;
    }

    try {
      setIsLoadingData(true);

      const [groupsResponse, instructorsResponse, parentsResponse, childrenResponse, childGroupAssignmentsResponse] = await Promise.all([
        fetch(`${API_BASE_URL}/manager-groups/groups`, {
          headers: buildAuthHeaders(authToken),
        }),
        fetch(`${API_BASE_URL}/instructor/manager-list`, {
          headers: buildAuthHeaders(authToken),
        }),
        fetch(`${API_BASE_URL}/parent/manager-list`, {
          headers: buildAuthHeaders(authToken),
        }),
        fetch(`${API_BASE_URL}/children`, {
          headers: buildAuthHeaders(authToken),
        }),
        fetch(`${API_BASE_URL}/manager-groups/children`, {
          headers: buildAuthHeaders(authToken),
        }),
      ]);

      const [groupsPayload, instructorsPayload, parentsPayload, childrenPayload, childGroupAssignmentsPayload] = await Promise.all([
        groupsResponse.json().catch(() => null),
        instructorsResponse.json().catch(() => null),
        parentsResponse.json().catch(() => null),
        childrenResponse.json().catch(() => null),
        childGroupAssignmentsResponse.json().catch(() => null),
      ]);

      if (
        !groupsResponse.ok
        || !instructorsResponse.ok
        || !parentsResponse.ok
        || !childrenResponse.ok
        || !childGroupAssignmentsResponse.ok
      ) {
        const message = groupsPayload?.message
          || instructorsPayload?.message
          || parentsPayload?.message
          || childrenPayload?.message
          || childGroupAssignmentsPayload?.message
          || 'לא ניתן לטעון נתוני מנהל כרגע.';
        throw new Error(message);
      }

      const normalizedGroups = (Array.isArray(groupsPayload) ? groupsPayload : [])
        .map((group) => ({
          groupId: Number(group?.groupId || 0),
          name: String(group?.name || ''),
          description: String(group?.description || ''),
          instructorId: group?.instructorId ? Number(group.instructorId) : null,
          instructorFullName: String(group?.instructorFullName || ''),
          instructorEmail: String(group?.instructorEmail || ''),
          activeChildrenCount: Number(group?.activeChildrenCount || 0),
        }))
        .filter((group) => group.groupId > 0);

      const normalizedInstructors = (Array.isArray(instructorsPayload) ? instructorsPayload : [])
        .map((instructor) => ({
          instructorId: Number(instructor?.instructorId || 0),
          firstName: String(instructor?.firstName || '').trim(),
          lastName: String(instructor?.lastName || '').trim(),
          fullName: String(instructor?.fullName || '').trim()
            || `${instructor?.firstName || ''} ${instructor?.lastName || ''}`.trim(),
          email: String(instructor?.email || ''),
          groupNames: Array.isArray(instructor?.groupNames)
            ? instructor.groupNames.map((name) => String(name || '').trim()).filter(Boolean)
            : [],
        }))
        .filter((instructor) => instructor.instructorId > 0);

      const normalizedParents = (Array.isArray(parentsPayload) ? parentsPayload : [])
        .map((parent) => {
          const parentId = Number(parent?.parentId || parent?.id || 0);
          const emailRaw = String(parent?.email || '').trim();
          const phoneRaw = String(parent?.phone || '').trim();
          const firstName = String(parent?.firstName || '').trim();
          const lastName = String(parent?.lastName || '').trim();
          const fullName = String(parent?.fullName || '').trim() || `${firstName} ${lastName}`.trim() || 'ללא שם';

          return {
            parentId,
            email: emailRaw.toLowerCase(),
            phone: phoneRaw,
            firstName,
            lastName,
            fullName,
            childNames: Array.isArray(parent?.childNames)
              ? parent.childNames.map((name) => String(name || '').trim()).filter(Boolean)
              : [],
            displayLabel: `${emailRaw || 'ללא אימייל'} - ${fullName}`,
          };
        })
        .filter((parent) => parent.parentId > 0);

      const normalizedChildren = (Array.isArray(childrenPayload) ? childrenPayload : [])
        .map((child) => {
          const childId = Number(child?.id || child?.childId || 0);
          const firstName = String(child?.firstName || '').trim();
          const lastName = String(child?.lastName || '').trim();
          const childDescription = String(child?.childDescription || '').trim();
          const parentEmail = String(child?.parentEmail || '').trim();
          const parentFullName = String(child?.parentFullName || '').trim();
          const fullName = `${firstName} ${lastName}`.trim() || `ילד ${childId}`;
          const birthDateRaw = String(child?.birthDate || '').trim();
          const parentNameForDisplay = parentFullName || 'ללא שם';
          const parentEmailSuffix = parentEmail ? ` - ${parentEmail}` : '';

          return {
            childId,
            parentId: Number(child?.parentId || 0),
            firstName,
            lastName,
            fullName,
            birthDate: birthDateRaw ? birthDateRaw.slice(0, 10) : '',
            childDescription,
            parentEmail,
            parentFullName,
            parentDisplayLabel: `הורה: ${parentNameForDisplay}${parentEmailSuffix}`,
            strengths: child?.strengths || '',
            weaknesses: child?.weaknesses || '',
            personalGoals: child?.personalGoals || '',
          };
        })
        .filter((child) => child.childId > 0);

      const parentNameByChildId = new Map(
        normalizedChildren.map((child) => [child.childId, child.parentFullName])
      );

      const normalizedChildGroupAssignments = (Array.isArray(childGroupAssignmentsPayload) ? childGroupAssignmentsPayload : [])
        .map((child) => {
          const childId = Number(child?.childId || child?.id || 0);
          const activeGroupId = child?.activeGroupId ? Number(child.activeGroupId) : null;
          const activeGroupName = String(child?.activeGroupName || '').trim();

          return {
            childId,
            activeGroupId,
            activeGroupName,
          };
        })
        .filter((child) => child.childId > 0);

      const childAssignmentByChildId = new Map(
        normalizedChildGroupAssignments.map((child) => [child.childId, child])
      );
      const groupById = new Map(normalizedGroups.map((group) => [group.groupId, group]));

      const normalizedChildrenWithAssignments = normalizedChildren.map((child) => {
        const assignment = childAssignmentByChildId.get(child.childId) || null;
        const activeGroupId = assignment?.activeGroupId || null;
        const activeGroupName = String(assignment?.activeGroupName || '').trim();
        const activeGroup = activeGroupId ? groupById.get(activeGroupId) : null;
        const assignedInstructorFullName = String(activeGroup?.instructorFullName || '').trim();
        const assignedInstructorEmail = String(activeGroup?.instructorEmail || '').trim();
        const assignedInstructorDisplay = assignedInstructorEmail && assignedInstructorFullName
          ? `${assignedInstructorEmail} - ${assignedInstructorFullName}`
          : assignedInstructorEmail || assignedInstructorFullName || '';

        return {
          ...child,
          activeGroupId,
          activeGroupName,
          assignedInstructorFullName,
          assignedInstructorEmail,
          assignedInstructorDisplay,
        };
      });

      setGroups(normalizedGroups);
      setInstructors(normalizedInstructors);
      setParents(normalizedParents);
      setChildren(normalizedChildrenWithAssignments);

      setSelectedInstructorId((prev) => (
        normalizedInstructors.some((instructor) => instructor.instructorId === prev) ? prev : null
      ));
    } catch (error) {
      Alert.alert('שגיאת טעינה', error?.message || 'לא ניתן לטעון נתונים כרגע.');
    } finally {
      setIsLoadingData(false);
    }
  }, [authToken, hasManagerAuth]);

  useEffect(() => {
    loadGroupsAndInstructors();
  }, [loadGroupsAndInstructors]);

  useEffect(() => {
    if (!hasInstructors) {
      setSelectedInstructorId(null);
      setIsCreateInstructorDropdownOpen(false);
    }
  }, [hasInstructors]);

  useEffect(() => {
    setExpandedGroupChildrenId((prevGroupId) => {
      if (!prevGroupId) {
        return null;
      }

      const exists = groups.some((group) => group.groupId === prevGroupId);
      return exists ? prevGroupId : null;
    });
  }, [groups]);

  useEffect(() => {
    const hasSearchTerm = searchEditChildGroupText.trim().length > 0;
    if (!hasSearchTerm) {
      return;
    }

    setIsEditChildGroupDropdownOpen(true);
    setIsEditChildParentDropdownOpen(false);
  }, [searchEditChildGroupText]);

  const openCreateGroupModal = () => {
    if (!ensureManagerAuth('ליצור קבוצה')) {
      return;
    }

    setCreateGroupErrors({ name: '', description: '', instructor: '' });
    setShowCreateGroupModal(true);
  };

  const closeCreateGroupModal = () => {
    setShowCreateGroupModal(false);
    setNewGroupName('');
    setNewGroupDescription('');
    setSelectedInstructorId(null);
    setIsCreateInstructorDropdownOpen(false);
    setCreateGroupErrors({ name: '', description: '', instructor: '' });
  };

  const openCreateInstructorModal = () => {
    if (!ensureManagerAuth('ליצור מדריך')) {
      return;
    }

    setCreateInstructorErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
    });
    setShowCreateInstructorModal(true);
  };

  const closeCreateInstructorModal = () => {
    setShowCreateInstructorModal(false);
    setNewInstructorFirstName('');
    setNewInstructorLastName('');
    setNewInstructorEmail('');
    setNewInstructorPassword('');
    setCreateInstructorErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
    });
  };

  const openCreateParentModal = () => {
    if (!ensureManagerAuth('ליצור הורה')) {
      return;
    }

    setCreateParentErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      phone: '',
    });
    setShowCreateParentModal(true);
  };

  const closeCreateParentModal = () => {
    setShowCreateParentModal(false);
    setNewParentFirstName('');
    setNewParentLastName('');
    setNewParentEmail('');
    setNewParentPassword('');
    setNewParentPhone('');
    setCreateParentErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      phone: '',
    });
  };

  const openCreateChildModal = () => {
    if (!ensureManagerAuth('ליצור ילד')) {
      return;
    }

    setCreateChildErrors({
      firstName: '',
      lastName: '',
      birthDate: '',
      childDescription: '',
      parentId: '',
    });
    setShowCreateChildModal(true);
  };

  const closeCreateChildModal = () => {
    setShowCreateChildModal(false);
    setNewChildFirstName('');
    setNewChildLastName('');
    setNewChildBirthDate('');
    setNewChildDescription('');
    setNewChildParentId(null);
    setNewChildStrengths([]);
    setNewChildWeaknesses([]);
    setIsCreateChildParentDropdownOpen(false);
    setCreateChildErrors({
      firstName: '',
      lastName: '',
      birthDate: '',
      childDescription: '',
      parentId: '',
    });
  };

  const createGroup = async () => {
    if (!ensureManagerAuth('ליצור קבוצה')) {
      return;
    }

    const normalizedGroupName = newGroupName.trim();
    const normalizedDescription = newGroupDescription.trim();
    const nextErrors = { name: '', description: '', instructor: '' };

    if (!normalizedGroupName) {
      nextErrors.name = 'יש להזין שם קבוצה לפני יצירה.';
    }

    if (!normalizedDescription) {
      nextErrors.description = 'יש להזין תיאור לקבוצה לפני יצירה.';
    }

    if (!selectedInstructorId) {
      nextErrors.instructor = hasInstructors
        ? 'יש לבחור מדריך לקבוצה החדשה.'
        : 'לא קיימים מדריכים פעילים במערכת. הוסיפו מדריך לפני יצירת קבוצה.';
    }

    if (nextErrors.name || nextErrors.description || nextErrors.instructor) {
      setCreateGroupErrors(nextErrors);
      Alert.alert('לא ניתן ליצור קבוצה', nextErrors.name || nextErrors.description || nextErrors.instructor);
      return;
    }

    setCreateGroupErrors({ name: '', description: '', instructor: '' });

    try {
      setIsSavingGroup(true);

      const response = await fetch(`${API_BASE_URL}/manager-groups/groups`, {
        method: 'POST',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          name: normalizedGroupName,
          description: normalizedDescription,
          instructorId: selectedInstructorId,
        }),
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message || 'יצירת קבוצה נכשלה.');
      }

      closeCreateGroupModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'הקבוצה נוצרה והמדריך שויך בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן ליצור קבוצה כרגע.');
    } finally {
      setIsSavingGroup(false);
    }
  };

  const createInstructor = async () => {
    if (!ensureManagerAuth('ליצור מדריך')) {
      return;
    }

    const firstName = newInstructorFirstName.trim();
    const lastName = newInstructorLastName.trim();
    const email = newInstructorEmail.trim().toLowerCase();
    const password = newInstructorPassword;

    const nextErrors = {
      firstName: '',
      lastName: '',
      email: '',
      password: '',
    };

    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (!email) {
      nextErrors.email = 'יש להזין אימייל.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      nextErrors.email = 'יש להזין כתובת אימייל תקינה.';
    } else if (instructors.some((instructor) => String(instructor.email || '').trim().toLowerCase() === email)) {
      nextErrors.email = 'כבר קיים מדריך עם כתובת האימייל הזו.';
    }

    if (!password) {
      nextErrors.password = 'יש להזין סיסמה.';
    } else if (password.length < 6) {
      nextErrors.password = 'הסיסמה חייבת להכיל לפחות 6 תווים.';
    }

    if (nextErrors.firstName || nextErrors.lastName || nextErrors.email || nextErrors.password) {
      setCreateInstructorErrors(nextErrors);
      Alert.alert(
        'לא ניתן ליצור מדריך',
        nextErrors.firstName || nextErrors.lastName || nextErrors.email || nextErrors.password
      );
      return;
    }

    setCreateInstructorErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
    });

    try {
      setIsSavingInstructor(true);

      const response = await fetch(`${API_BASE_URL}/instructor/manager-create`, {
        method: 'POST',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          firstName,
          lastName,
          email,
          password,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 409) {
          throw new Error('כבר קיים משתמש עם כתובת האימייל הזו.');
        }

        throw new Error(payload?.message || 'יצירת מדריך נכשלה.');
      }

      closeCreateInstructorModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'המדריך נוצר בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן ליצור מדריך כרגע.');
    } finally {
      setIsSavingInstructor(false);
    }
  };

  const createParent = async () => {
    if (!ensureManagerAuth('ליצור הורה')) {
      return;
    }

    const firstName = newParentFirstName.trim();
    const lastName = newParentLastName.trim();
    const email = newParentEmail.trim().toLowerCase();
    const password = newParentPassword;
    const phone = newParentPhone.trim();

    const nextErrors = {
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      phone: '',
    };

    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (!email) {
      nextErrors.email = 'יש להזין אימייל.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      nextErrors.email = 'יש להזין כתובת אימייל תקינה.';
    } else if (parents.some((parent) => parent.email === email)) {
      nextErrors.email = 'כבר קיים הורה עם כתובת האימייל הזו.';
    }

    if (!password) {
      nextErrors.password = 'יש להזין סיסמה.';
    } else if (password.length < 6) {
      nextErrors.password = 'הסיסמה חייבת להכיל לפחות 6 תווים.';
    }

    if (phone && !/^[-+()\d\s]{7,20}$/.test(phone)) {
      nextErrors.phone = 'מספר הטלפון אינו תקין.';
    }

    if (nextErrors.firstName || nextErrors.lastName || nextErrors.email || nextErrors.password || nextErrors.phone) {
      setCreateParentErrors(nextErrors);
      Alert.alert(
        'לא ניתן ליצור הורה',
        nextErrors.firstName || nextErrors.lastName || nextErrors.email || nextErrors.password || nextErrors.phone
      );
      return;
    }

    setCreateParentErrors({
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      phone: '',
    });

    try {
      setIsSavingParent(true);

      const response = await fetch(`${API_BASE_URL}/parent/manager-create`, {
        method: 'POST',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          firstName,
          lastName,
          email,
          password,
          phone,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 409) {
          throw new Error('כבר קיים משתמש עם כתובת האימייל הזו.');
        }

        throw new Error(payload?.message || 'יצירת הורה נכשלה.');
      }

      closeCreateParentModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'ההורה נוצר בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן ליצור הורה כרגע.');
    } finally {
      setIsSavingParent(false);
    }
  };

  const isBirthDateValid = (birthDate) => {
    const trimmed = birthDate.trim();
    if (!trimmed) {
      return true;
    }

    if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
      return false;
    }

    const parsed = new Date(`${trimmed}T00:00:00Z`);
    return !Number.isNaN(parsed.getTime());
  };

  const createChild = async () => {
    if (!ensureManagerAuth('ליצור ילד')) {
      return;
    }

    const firstName = newChildFirstName.trim();
    const lastName = newChildLastName.trim();
    const birthDate = newChildBirthDate.trim();
    const childDescription = newChildDescription.trim();
    const parentId = Number(newChildParentId || 0);

    const nextErrors = {
      firstName: '',
      lastName: '',
      birthDate: '',
      childDescription: '',
      parentId: '',
    };

    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (!parentId) {
      nextErrors.parentId = hasParents
        ? 'יש לבחור הורה מהרשימה.'
        : 'לא קיימים הורים פעילים במערכת. צרו הורה לפני יצירת ילד.';
    }

    if (!childDescription) {
      nextErrors.childDescription = 'יש להזין תיאור מקצועי לילד לצורך המלצת AI.';
    }

    if (!isBirthDateValid(birthDate)) {
      nextErrors.birthDate = 'יש להזין תאריך בפורמט YYYY-MM-DD.';
    }

    if (nextErrors.firstName || nextErrors.lastName || nextErrors.parentId || nextErrors.birthDate || nextErrors.childDescription) {
      setCreateChildErrors(nextErrors);
      Alert.alert(
        'לא ניתן ליצור ילד',
        nextErrors.firstName || nextErrors.lastName || nextErrors.parentId || nextErrors.birthDate || nextErrors.childDescription
      );
      return;
    }

    setCreateChildErrors({
      firstName: '',
      lastName: '',
      birthDate: '',
      childDescription: '',
      parentId: '',
    });

    try {
      setIsSavingChild(true);

      const response = await fetch(`${API_BASE_URL}/children`, {
        method: 'POST',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          parentId,
          firstName,
          lastName,
          birthDate: birthDate || null,
          childDescription,
          strengths: newChildStrengths.length > 0 ? newChildStrengths : null,
          weaknesses: newChildWeaknesses.length > 0 ? newChildWeaknesses : null,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'יצירת ילד נכשלה.');
      }

      const childId = Number(payload?.id || payload?.childId || payload?.child?.id || 0);
      let recommendationText = '';

      if (childId > 0) {
        const recommendationResponse = await fetch(`${API_BASE_URL}/manager-groups/recommend-group`, {
          method: 'POST',
          headers: buildJsonAuthHeaders(authToken),
          body: JSON.stringify({
            childId,
            childDescription,
          }),
        });

        const recommendationPayload = await recommendationResponse.json().catch(() => null);
        if (recommendationResponse.ok) {
          const groupName = String(recommendationPayload?.recommendedGroupName || '').trim();
          const reasoning = String(recommendationPayload?.reasoning || '').trim();
          if (groupName) {
            recommendationText = reasoning
              ? `\n\nהמלצת AI לקבוצה: ${groupName}\n${reasoning}`
              : `\n\nהמלצת AI לקבוצה: ${groupName}`;
          }
        }
      }

      closeCreateChildModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', `הילד נוצר בהצלחה.${recommendationText}`);
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן ליצור ילד כרגע.');
    } finally {
      setIsSavingChild(false);
    }
  };

  const filteredInstructors = useMemo(() => {
    const term = searchInstructorText.trim().toLowerCase();
    if (!term) {
      return instructors;
    }

    return instructors.filter((instructor) => {
      const searchableText = [
        instructor.fullName,
        instructor.email,
        Array.isArray(instructor.groupNames) ? instructor.groupNames.join(' ') : '',
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [instructors, searchInstructorText]);

  const filteredParents = useMemo(() => {
    const term = searchParentText.trim().toLowerCase();
    if (!term) {
      return parents;
    }

    return parents.filter((parent) => {
      const phoneDigits = String(parent.phone || '').replace(/\D/g, '');
      const searchableText = [
        parent.fullName,
        parent.email,
        parent.phone,
        phoneDigits,
        Array.isArray(parent.childNames) ? parent.childNames.join(' ') : '',
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [parents, searchParentText]);

  const filteredChildren = useMemo(() => {
    const term = searchChildText.trim().toLowerCase();
    if (!term) {
      return children;
    }

    return children.filter((child) => {
      const searchableText = [
        child.fullName,
        child.parentEmail,
        child.parentFullName,
        child.birthDate,
        child.activeGroupName,
        child.assignedInstructorDisplay,
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [children, searchChildText]);

  const filteredGroups = useMemo(() => {
    const term = searchGroupText.trim().toLowerCase();
    if (!term) {
      return groups;
    }

    return groups.filter((group) => {
      const searchableText = [
        group.name,
        group.description,
        group.instructorFullName,
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [groups, searchGroupText]);

  const childNamesByGroupId = useMemo(() => {
    const namesByGroup = new Map();

    children.forEach((child) => {
      const groupId = Number(child?.activeGroupId || 0);
      const childFullName = String(child?.fullName || '').trim();

      if (groupId <= 0 || !childFullName) {
        return;
      }

      const names = namesByGroup.get(groupId) || [];
      names.push(childFullName);
      namesByGroup.set(groupId, names);
    });

    namesByGroup.forEach((names, groupId) => {
      const uniqueSortedNames = Array.from(new Set(names))
        .sort((leftName, rightName) => leftName.localeCompare(rightName, 'he'));
      namesByGroup.set(groupId, uniqueSortedNames);
    });

    return namesByGroup;
  }, [children]);

  const filteredGroupsForChildEdit = useMemo(() => {
    const term = searchEditChildGroupText.trim().toLowerCase();
    if (!term) {
      return groups;
    }

    return groups.filter((group) => {
      const searchableText = [
        group.name,
        group.description,
        group.instructorFullName,
        group.instructorEmail,
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [groups, searchEditChildGroupText]);

  const filteredInstructorsForGroupEdit = useMemo(() => {
    const term = searchEditGroupInstructorText.trim().toLowerCase();
    if (!term) {
      return instructors;
    }

    return instructors.filter((instructor) => {
      const searchableText = [
        instructor.fullName,
        instructor.email,
        Array.isArray(instructor.groupNames) ? instructor.groupNames.join(' ') : '',
      ].join(' ').toLowerCase();

      return searchableText.includes(term);
    });
  }, [instructors, searchEditGroupInstructorText]);

  const openEditInstructorModal = (instructor) => {
    if (!ensureManagerAuth('לעדכן מדריך')) {
      return;
    }

    setEditInstructorId(instructor.instructorId);
    setEditInstructorFirstName(instructor.firstName || '');
    setEditInstructorLastName(instructor.lastName || '');
    setEditInstructorEmail(instructor.email || '');
    setEditInstructorErrors({ firstName: '', lastName: '' });
    setShowEditInstructorModal(true);
  };

  const closeEditInstructorModal = () => {
    setShowEditInstructorModal(false);
    setEditInstructorId(null);
    setEditInstructorFirstName('');
    setEditInstructorLastName('');
    setEditInstructorEmail('');
    setEditInstructorErrors({ firstName: '', lastName: '' });
  };

  const updateInstructor = async () => {
    const firstName = editInstructorFirstName.trim();
    const lastName = editInstructorLastName.trim();

    const nextErrors = { firstName: '', lastName: '' };
    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (nextErrors.firstName || nextErrors.lastName) {
      setEditInstructorErrors(nextErrors);
      Alert.alert('לא ניתן לעדכן מדריך', nextErrors.firstName || nextErrors.lastName);
      return;
    }

    try {
      setIsUpdatingInstructor(true);

      const response = await fetch(`${API_BASE_URL}/instructor/manager-update/${editInstructorId}`, {
        method: 'PUT',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({ firstName, lastName }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'עדכון מדריך נכשל.');
      }

      closeEditInstructorModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'פרטי המדריך עודכנו בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן לעדכן מדריך כרגע.');
    } finally {
      setIsUpdatingInstructor(false);
    }
  };

  const confirmDeleteInstructor = (instructor) => {
    if (!ensureManagerAuth('למחוק מדריך')) {
      return;
    }

    const displayName = instructor.fullName || instructor.email || 'המדריך';

    const assignedGroups = groups.filter((group) => group.instructorId === instructor.instructorId);

    const deleteInstructorOnly = async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/instructor/manager-delete/${instructor.instructorId}`, {
          method: 'DELETE',
          headers: buildAuthHeaders(authToken),
        });

        const payload = await parseApiPayload(response);
        if (!response.ok) {
          throw new Error(payload?.message || 'מחיקת מדריך נכשלה.');
        }

        await loadGroupsAndInstructors();
        Alert.alert('הצלחה', 'המדריך נמחק בהצלחה.');
      } catch (error) {
        Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן למחוק מדריך כרגע.');
      }
    };

    if (assignedGroups.length === 0) {
      confirmDestructiveAction(
        'מחיקת מדריך',
        `המדריך ${displayName} לא משויך לקבוצות. האם למחוק את המדריך?`,
        deleteInstructorOnly,
      );
      return;
    }

    const assignedGroupNames = assignedGroups
      .map((group) => group.name || `קבוצה ${group.groupId}`)
      .join(', ');

    const deleteAssignedGroupsAndInstructor = async () => {
      try {
        for (const group of assignedGroups) {
          const groupResponse = await fetch(`${API_BASE_URL}/manager-groups/groups/${group.groupId}`, {
            method: 'DELETE',
            headers: buildAuthHeaders(authToken),
          });

          const groupPayload = await parseApiPayload(groupResponse);
          if (!groupResponse.ok) {
            throw new Error(groupPayload?.message || `מחיקת הקבוצה "${group.name || `קבוצה ${group.groupId}`}" נכשלה.`);
          }
        }

        await deleteInstructorOnly();
      } catch (error) {
        Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן למחוק את הקבוצות של המדריך כרגע.');
      }
    };

    const warningTitle = 'המדריך משויך לקבוצות';
    const warningMessage = `למדריך ${displayName} משויכות ${assignedGroups.length} קבוצות: ${assignedGroupNames}.\n\nכדי למחוק את המדריך יש למחוק גם את הקבוצות המשויכות אליו. האם להמשיך?`;

    if (Platform.OS === 'web') {
      const confirmed = typeof window !== 'undefined'
        ? window.confirm(`${warningTitle}\n\n${warningMessage}`)
        : false;

      if (confirmed) {
        deleteAssignedGroupsAndInstructor();
      }
      return;
    }

    Alert.alert(
      warningTitle,
      warningMessage,
      [
        { text: 'ביטול', style: 'cancel' },
        {
          text: 'מחיקת קבוצות ומדריך',
          style: 'destructive',
          onPress: deleteAssignedGroupsAndInstructor,
        },
      ],
      { cancelable: true },
    );
  };

  const openEditParentModal = (parent) => {
    if (!ensureManagerAuth('לעדכן הורה')) {
      return;
    }

    setEditParentId(parent.parentId);
    setEditParentFirstName(parent.firstName || '');
    setEditParentLastName(parent.lastName || '');
    setEditParentEmail(parent.email || '');
    setEditParentErrors({ firstName: '', lastName: '' });
    setShowEditParentModal(true);
  };

  const closeEditParentModal = () => {
    setShowEditParentModal(false);
    setEditParentId(null);
    setEditParentFirstName('');
    setEditParentLastName('');
    setEditParentEmail('');
    setEditParentErrors({ firstName: '', lastName: '' });
  };

  const updateParent = async () => {
    const firstName = editParentFirstName.trim();
    const lastName = editParentLastName.trim();

    const nextErrors = { firstName: '', lastName: '' };
    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (nextErrors.firstName || nextErrors.lastName) {
      setEditParentErrors(nextErrors);
      Alert.alert('לא ניתן לעדכן הורה', nextErrors.firstName || nextErrors.lastName);
      return;
    }

    try {
      setIsUpdatingParent(true);

      const response = await fetch(`${API_BASE_URL}/parent/manager-update/${editParentId}`, {
        method: 'PUT',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({ firstName, lastName }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'עדכון הורה נכשל.');
      }

      closeEditParentModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'פרטי ההורה עודכנו בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן לעדכן הורה כרגע.');
    } finally {
      setIsUpdatingParent(false);
    }
  };

  const confirmDeleteParent = (parent) => {
    if (!ensureManagerAuth('למחוק הורה')) {
      return;
    }

    const displayName = parent.fullName || parent.email || 'ההורה';

    confirmDestructiveAction('מחיקת הורה', `האם למחוק את ${displayName}?`, async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/parent/manager-delete/${parent.parentId}`, {
          method: 'DELETE',
          headers: buildAuthHeaders(authToken),
        });

        const payload = await parseApiPayload(response);
        if (!response.ok) {
          throw new Error(payload?.message || 'מחיקת הורה נכשלה.');
        }

        await loadGroupsAndInstructors();
        Alert.alert('הצלחה', 'ההורה נמחק בהצלחה.');
      } catch (error) {
        Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן למחוק הורה כרגע.');
      }
    });
  };

  const openEditChildModal = (child) => {
    if (!ensureManagerAuth('לעדכן ילד')) {
      return;
    }

    const activeGroupId = child?.activeGroupId ? Number(child.activeGroupId) : null;

    setEditChildId(child.childId);
    setEditChildFirstName(child.firstName || '');
    setEditChildLastName(child.lastName || '');
    setEditChildBirthDate(child.birthDate || '');
    setEditChildDescription(child.childDescription || '');
    setEditChildParentId(child.parentId || null);
    setEditChildStrengths(child.strengths ? (typeof child.strengths === 'string' ? JSON.parse(child.strengths) : child.strengths) : []);
    setEditChildWeaknesses(child.weaknesses ? (typeof child.weaknesses === 'string' ? JSON.parse(child.weaknesses) : child.weaknesses) : []);
    setEditChildGroupId(activeGroupId);
    setOriginalEditChildGroupId(activeGroupId);
    setOriginalEditChildGroupName(String(child.activeGroupName || '').trim());
    setSearchEditChildGroupText('');
    setIsEditChildGroupDropdownOpen(false);
    setEditChildAiRecommendation(null);
    setIsFindingAiGroupRecommendation(false);
    setIsApplyingAiRecommendedGroup(false);
    setIsEditChildParentDropdownOpen(false);
    setEditChildErrors({ firstName: '', lastName: '', birthDate: '', parentId: '' });
    setShowEditChildModal(true);
  };

  const closeEditChildModal = () => {
    setShowEditChildModal(false);
    setEditChildId(null);
    setEditChildFirstName('');
    setEditChildLastName('');
    setEditChildBirthDate('');
    setEditChildDescription('');
    setEditChildParentId(null);
    setEditChildStrengths([]);
    setEditChildWeaknesses([]);
    setEditChildGroupId(null);
    setOriginalEditChildGroupId(null);
    setOriginalEditChildGroupName('');
    setSearchEditChildGroupText('');
    setIsEditChildGroupDropdownOpen(false);
    setEditChildAiRecommendation(null);
    setIsFindingAiGroupRecommendation(false);
    setIsApplyingAiRecommendedGroup(false);
    setIsEditChildParentDropdownOpen(false);
    setEditChildErrors({ firstName: '', lastName: '', birthDate: '', parentId: '' });
  };

  const selectGroupForChildEdit = (group) => {
    if (!group?.groupId) {
      return;
    }

    const isCurrentGroup = Boolean(originalEditChildGroupId && group.groupId === originalEditChildGroupId);
    if (isCurrentGroup) {
      return;
    }

    const applySelection = () => {
      setEditChildGroupId(group.groupId);
      setIsEditChildGroupDropdownOpen(false);
    };

    if (!originalEditChildGroupId) {
      applySelection();
      return;
    }

    const currentGroupName = originalEditChildGroupName || `קבוצה ${originalEditChildGroupId}`;
    const warningMessage = `הילד כבר משויך לקבוצה: "${currentGroupName}". האם להעביר אותו לקבוצה אחרת?`;

    if (Platform.OS === 'web') {
      const confirmed = typeof window !== 'undefined' ? window.confirm(warningMessage) : false;
      if (confirmed) {
        applySelection();
      }

      return;
    }

    Alert.alert(
      'אישור העברת ילד לקבוצה',
      warningMessage,
      [
        { text: 'ביטול', style: 'cancel' },
        { text: 'כן, להעביר', style: 'destructive', onPress: applySelection },
      ]
    );
  };

  const selectNoGroupForChildEdit = () => {
    const applySelection = () => {
      setEditChildGroupId(null);
      setIsEditChildGroupDropdownOpen(false);
    };

    if (!originalEditChildGroupId) {
      applySelection();
      return;
    }

    const currentGroupName = originalEditChildGroupName || `קבוצה ${originalEditChildGroupId}`;
    const warningMessage = `הילד כבר משויך לקבוצה: "${currentGroupName}". האם להסיר את השיוך לקבוצה?`;

    if (Platform.OS === 'web') {
      const confirmed = typeof window !== 'undefined' ? window.confirm(warningMessage) : false;
      if (confirmed) {
        applySelection();
      }

      return;
    }

    Alert.alert(
      'אישור הסרת שיוך לקבוצה',
      warningMessage,
      [
        { text: 'ביטול', style: 'cancel' },
        { text: 'כן, להסיר', style: 'destructive', onPress: applySelection },
      ]
    );
  };

  const findAiGroupRecommendationForChildEdit = async () => {
    if (!ensureManagerAuth('למצוא קבוצה באמצעות בינה מלאכותית')) {
      return;
    }

    if (!editChildId) {
      Alert.alert('שגיאה', 'לא נבחר ילד לעדכון.');
      return;
    }

    const childDescription = editChildDescription.trim();
    if (!childDescription) {
      Alert.alert('שדות חסרים', 'יש להזין תיאור מקצועי לפני הפעלת המלצת AI.');
      return;
    }

    const NO_ACTIVE_GROUPS_REASON_HE = 'לא נמצאו קבוצות פעילות עם מדריך משויך.';
    const NO_ACTIVE_GROUPS_REASON_EN = 'No active groups with assigned instructor were found';

    const showAiMessage = (title, message) => {
      if (Platform.OS === 'web' && typeof window !== 'undefined') {
        window.alert(`${title}\n${message}`);
        return;
      }

      Alert.alert(title, message);
    };

    const requestController = typeof AbortController !== 'undefined' ? new AbortController() : null;
    let timeoutId = null;

    try {
      setIsFindingAiGroupRecommendation(true);

      if (requestController) {
        timeoutId = setTimeout(() => {
          requestController.abort();
        }, 90000);
      }

      const response = await fetch(`${API_BASE_URL}/manager-groups/recommend-group`, {
        method: 'POST',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          childId: editChildId,
          childDescription,
        }),
        signal: requestController ? requestController.signal : undefined,
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן להפיק המלצת קבוצה כרגע.');
      }

      if (payload?.aiUnavailable) {
        showAiMessage(
          'המלצת AI לא זמינה',
          String(payload?.message || 'לא הצלחנו לקבל תשובה ממנוע ה-AI, תנסה שוב מאוחר יותר...')
        );
        setEditChildAiRecommendation(null);
        return;
      }

      const recommendedGroupId = Number(payload?.recommendedGroupId || 0);
      if (!recommendedGroupId) {
        throw new Error(payload?.message || 'המלצת ה-AI לא כללה קבוצה תקינה.');
      }

      const currentAssignedGroupId = Number(originalEditChildGroupId || 0);
      if (currentAssignedGroupId > 0 && recommendedGroupId === currentAssignedGroupId) {
        const currentGroupName = originalEditChildGroupName
          || groups.find((group) => group.groupId === currentAssignedGroupId)?.name
          || `קבוצה ${currentAssignedGroupId}`;

        setEditChildAiRecommendation(null);
        showAiMessage(
          'התאמה מצוינת',
          `הבינה המלאכותית מצאה שהקבוצה המתאימה ביותר לילד היא הקבוצה הנוכחית שלו: ${currentGroupName}.`
        );
        return;
      }

      setEditChildAiRecommendation({
        recommendedGroupId,
        recommendedGroupName: String(payload?.recommendedGroupName || ''),
        recommendedInstructorFullName: String(payload?.recommendedInstructorFullName || ''),
        score: Number(payload?.score || 0),
        reasoning: String(payload?.reasoning || ''),
        usedFallback: Boolean(payload?.usedFallback),
        sharedTerms: Array.isArray(payload?.sharedTerms)
          ? payload.sharedTerms.map((term) => String(term || '').trim()).filter(Boolean)
          : [],
        sharedConcepts: Array.isArray(payload?.sharedConcepts)
          ? payload.sharedConcepts.map((concept) => String(concept || '').trim()).filter(Boolean)
          : [],
      });
    } catch (error) {
      const isTimeout = error?.name === 'AbortError';
      const rawMessage = isTimeout
        ? 'בקשת ההמלצה ארכה יותר מדי זמן. נסו שוב מאוחר יותר.'
        : (error?.message || 'לא ניתן להפיק המלצת קבוצה כרגע.');

      const normalizedRawMessage = String(rawMessage).trim().toLowerCase();
      const message = (
        normalizedRawMessage === NO_ACTIVE_GROUPS_REASON_HE.toLowerCase()
        || normalizedRawMessage === NO_ACTIVE_GROUPS_REASON_EN.toLowerCase()
        || normalizedRawMessage === `${NO_ACTIVE_GROUPS_REASON_EN.toLowerCase()}.`
      )
        ? NO_ACTIVE_GROUPS_REASON_HE
        : rawMessage;

      showAiMessage('המלצה נכשלה', message);
      setEditChildAiRecommendation(null);
    } finally {
      if (timeoutId) {
        clearTimeout(timeoutId);
      }

      setIsFindingAiGroupRecommendation(false);
    }
  };

  const applyAiRecommendedGroupForChildEdit = () => {
    if (!ensureManagerAuth('לשייך ילד לקבוצה באמצעות המלצת AI')) {
      return;
    }

    const recommendedGroupId = Number(editChildAiRecommendation?.recommendedGroupId || 0);
    if (!editChildId || !recommendedGroupId) {
      Alert.alert('שגיאה', 'לא נמצאה המלצת קבוצה תקינה ליישום.');
      return;
    }

    if (recommendedGroupId === editChildGroupId) {
      const selectedGroupName = editChildAiRecommendation?.recommendedGroupName
        || groups.find((group) => group.groupId === recommendedGroupId)?.name
        || `קבוצה ${recommendedGroupId}`;

      if (originalEditChildGroupId && recommendedGroupId === originalEditChildGroupId) {
        Alert.alert(
          'התאמה מצוינת',
          `הקבוצה המתאימה ביותר לילד כבר משויכת אליו: ${selectedGroupName}. אין צורך לבצע שינוי נוסף.`
        );
      } else {
        Alert.alert('ללא שינוי', 'הקבוצה המומלצת כבר נבחרה בטופס.');
      }

      return;
    }

    const targetGroupName = editChildAiRecommendation?.recommendedGroupName
      || groups.find((group) => group.groupId === recommendedGroupId)?.name
      || `קבוצה ${recommendedGroupId}`;

    const applySelection = () => {
      setEditChildGroupId(recommendedGroupId);
      setIsEditChildGroupDropdownOpen(false);
      Alert.alert('הקבוצה נבחרה', `הקבוצה ${targetGroupName} נבחרה בטופס. השיוך יתבצע רק לאחר לחיצה על "שמירת שינויים".`);
    };

    if (originalEditChildGroupId) {
      const currentGroupName = originalEditChildGroupName || `קבוצה ${originalEditChildGroupId}`;
      const warningMessage = `הילד כבר משויך לקבוצה: "${currentGroupName}". האם להעביר אותו לקבוצה המומלצת?`;

      if (Platform.OS === 'web') {
        const confirmed = typeof window !== 'undefined' ? window.confirm(warningMessage) : false;
        if (confirmed) {
          applySelection();
        }

        return;
      }

      Alert.alert(
        'אישור העברת ילד לקבוצה המומלצת',
        warningMessage,
        [
          { text: 'ביטול', style: 'cancel' },
          { text: 'כן, להעביר', style: 'destructive', onPress: applySelection },
        ]
      );
      return;
    }

    applySelection();
  };

  const updateChild = async () => {
    const firstName = editChildFirstName.trim();
    const lastName = editChildLastName.trim();
    const birthDate = editChildBirthDate.trim();
    const childDescription = editChildDescription.trim();
    const parentId = Number(editChildParentId || 0);
    const nextGroupId = editChildGroupId ? Number(editChildGroupId) : null;
    const shouldAssignGroup = Boolean(nextGroupId && nextGroupId !== originalEditChildGroupId);
    const shouldUnassignGroup = Boolean(!nextGroupId && originalEditChildGroupId);

    const nextErrors = { firstName: '', lastName: '', birthDate: '', parentId: '' };
    if (!firstName) {
      nextErrors.firstName = 'יש להזין שם פרטי.';
    }

    if (!lastName) {
      nextErrors.lastName = 'יש להזין שם משפחה.';
    }

    if (!parentId) {
      nextErrors.parentId = 'יש לבחור הורה מהרשימה.';
    }

    if (!isBirthDateValid(birthDate)) {
      nextErrors.birthDate = 'יש להזין תאריך בפורמט YYYY-MM-DD.';
    }

    if (nextErrors.firstName || nextErrors.lastName || nextErrors.parentId || nextErrors.birthDate) {
      setEditChildErrors(nextErrors);
      Alert.alert('לא ניתן לעדכן ילד', nextErrors.firstName || nextErrors.lastName || nextErrors.parentId || nextErrors.birthDate);
      return;
    }

    try {
      setIsUpdatingChild(true);

      const response = await fetch(`${API_BASE_URL}/children/${editChildId}`, {
        method: 'PUT',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          parentId,
          firstName,
          lastName,
          birthDate: birthDate || null,
          childDescription: childDescription || null,
          isActive: true,
          strengths: editChildStrengths.length > 0 ? editChildStrengths : null,
          weaknesses: editChildWeaknesses.length > 0 ? editChildWeaknesses : null,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'עדכון ילד נכשל.');
      }

      if (shouldAssignGroup || shouldUnassignGroup) {
        const assignResponse = await fetch(`${API_BASE_URL}/manager-groups/assign-child`, {
          method: 'POST',
          headers: buildJsonAuthHeaders(authToken),
          body: JSON.stringify({
            childId: editChildId,
            groupId: nextGroupId,
          }),
        });

        const assignPayload = await assignResponse.json().catch(() => null);
        if (!assignResponse.ok) {
          throw new Error(
            `פרטי הילד עודכנו, אך עדכון שיוך לקבוצה נכשל: ${assignPayload?.message || 'לא ניתן לעדכן שיוך לקבוצה כרגע.'}`
          );
        }
      }

      const targetGroupName = shouldAssignGroup
        ? groups.find((group) => group.groupId === nextGroupId)?.name || ''
        : '';
      let successMessage = 'פרטי הילד עודכנו בהצלחה.';
      if (shouldAssignGroup) {
        successMessage = targetGroupName
          ? `פרטי הילד עודכנו בהצלחה והילד שובץ לקבוצה ${targetGroupName}.`
          : 'פרטי הילד עודכנו בהצלחה והשיוך לקבוצה הושלם.';
      } else if (shouldUnassignGroup) {
        successMessage = 'פרטי הילד עודכנו בהצלחה והילד אינו משויך לקבוצה כרגע.';
      }

      closeEditChildModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', successMessage);
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן לעדכן ילד כרגע.');
    } finally {
      setIsUpdatingChild(false);
    }
  };

  const confirmDeleteChild = (child) => {
    if (!ensureManagerAuth('למחוק ילד')) {
      return;
    }

    const displayName = child.fullName || `ילד ${child.childId}`;

    confirmDestructiveAction('מחיקת ילד', `האם למחוק את ${displayName}?`, async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/children/${child.childId}`, {
          method: 'DELETE',
          headers: buildAuthHeaders(authToken),
        });

        const payload = await parseApiPayload(response);
        if (!response.ok) {
          throw new Error(payload?.message || 'מחיקת ילד נכשלה.');
        }

        await loadGroupsAndInstructors();
        Alert.alert('הצלחה', 'הילד הושבת והוסר מהקבוצה בהצלחה.');
      } catch (error) {
        Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן למחוק ילד כרגע.');
      }
    });
  };

  const openEditGroupModal = (group) => {
    if (!ensureManagerAuth('לעדכן קבוצה')) {
      return;
    }

    setEditGroupId(group.groupId);
    setEditGroupName(group.name || '');
    setEditGroupDescription(group.description || '');
    setEditGroupInstructorId(group.instructorId || null);
    setSearchEditGroupInstructorText('');
    setIsEditGroupInstructorDropdownOpen(false);
    setEditGroupErrors({ name: '', description: '', instructor: '' });
    setShowEditGroupModal(true);
  };

  const closeEditGroupModal = () => {
    setShowEditGroupModal(false);
    setEditGroupId(null);
    setEditGroupName('');
    setEditGroupDescription('');
    setEditGroupInstructorId(null);
    setSearchEditGroupInstructorText('');
    setIsEditGroupInstructorDropdownOpen(false);
    setEditGroupErrors({ name: '', description: '', instructor: '' });
  };

  const updateGroup = async () => {
    if (!ensureManagerAuth('לעדכן קבוצה')) {
      return;
    }

    if (!editGroupId) {
      Alert.alert('שגיאה', 'לא נבחרה קבוצה לעדכון.');
      return;
    }

    const normalizedName = editGroupName.trim();
    const normalizedDescription = editGroupDescription.trim();
    const nextErrors = { name: '', description: '', instructor: '' };

    if (!normalizedName) {
      nextErrors.name = 'יש להזין שם קבוצה.';
    }

    if (!normalizedDescription) {
      nextErrors.description = 'יש להזין תיאור קבוצה.';
    }

    if (!editGroupInstructorId) {
      nextErrors.instructor = 'יש לבחור מדריך לקבוצה.';
    }

    if (nextErrors.name || nextErrors.description || nextErrors.instructor) {
      setEditGroupErrors(nextErrors);
      Alert.alert(
        'לא ניתן לעדכן קבוצה',
        nextErrors.name || nextErrors.description || nextErrors.instructor
      );
      return;
    }

    setEditGroupErrors({ name: '', description: '', instructor: '' });

    try {
      setIsUpdatingGroup(true);

      const response = await fetch(`${API_BASE_URL}/manager-groups/groups/${editGroupId}`, {
        method: 'PUT',
        headers: buildJsonAuthHeaders(authToken),
        body: JSON.stringify({
          name: normalizedName,
          description: normalizedDescription,
          instructorId: editGroupInstructorId,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'עדכון הקבוצה נכשל.');
      }

      closeEditGroupModal();
      await loadGroupsAndInstructors();
      Alert.alert('הצלחה', 'פרטי הקבוצה עודכנו בהצלחה.');
    } catch (error) {
      Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן לעדכן קבוצה כרגע.');
    } finally {
      setIsUpdatingGroup(false);
    }
  };

  const selectInstructorForGroupEdit = (instructor) => {
    if (!instructor?.instructorId) {
      return;
    }

    const isCurrentInstructor = Boolean(
      originalEditGroupInstructorId && instructor.instructorId === originalEditGroupInstructorId
    );
    if (isCurrentInstructor) {
      return;
    }

    const applySelection = () => {
      setEditGroupInstructorId(instructor.instructorId);
      setIsEditGroupInstructorDropdownOpen(false);
      if (editGroupErrors.instructor) {
        setEditGroupErrors((prev) => ({ ...prev, instructor: '' }));
      }
    };

    if (!originalEditGroupInstructorId) {
      applySelection();
      return;
    }

    const currentInstructorDisplay = originalEditGroupInstructorDisplay || `מדריך ${originalEditGroupInstructorId}`;
    const warningMessage = `הקבוצה כבר משויכת למדריך: "${currentInstructorDisplay}". אתה בטוח שאתה רוצה להחליף מדריך לקבוצה?`;

    if (Platform.OS === 'web') {
      const confirmed = typeof window !== 'undefined' ? window.confirm(warningMessage) : false;
      if (confirmed) {
        applySelection();
      }

      return;
    }

    Alert.alert(
      'אישור החלפת מדריך',
      warningMessage,
      [
        { text: 'ביטול', style: 'cancel' },
        { text: 'כן, להחליף', style: 'destructive', onPress: applySelection },
      ]
    );
  };

  const confirmDeleteGroup = (group) => {
    if (!ensureManagerAuth('למחוק קבוצה')) {
      return;
    }

    const displayName = group.name || `קבוצה ${group.groupId}`;

    confirmDestructiveAction('מחיקת קבוצה', `האם למחוק את ${displayName}?`, async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/manager-groups/groups/${group.groupId}`, {
          method: 'DELETE',
          headers: buildAuthHeaders(authToken),
        });

        const payload = await parseApiPayload(response);
        if (!response.ok) {
          throw new Error(payload?.message || 'מחיקת קבוצה נכשלה.');
        }

        await loadGroupsAndInstructors();
        Alert.alert('הצלחה', 'הקבוצה נמחקה בהצלחה.');
      } catch (error) {
        Alert.alert('פעולה נכשלה', error?.message || 'לא ניתן למחוק קבוצה כרגע.');
      }
    });
  };

  const managerActionItems = [
    {
      key: 'create-child',
      label: 'יצירת ילד',
      icon: '👶',
      tintStyle: styles.actionTintSea,
      offsetStyle: styles.actionOffsetA,
      onPress: openCreateChildModal,
      requiresAuth: true,
    },
    {
      key: 'create-instructor',
      label: 'יצירת מדריך',
      icon: '🪪',
      tintStyle: styles.actionTintLagoon,
      offsetStyle: styles.actionOffsetI,
      onPress: openCreateInstructorModal,
      requiresAuth: true,
    },
    {
      key: 'create-parent',
      label: 'יצירת הורה',
      icon: '👪',
      tintStyle: styles.actionTintSky,
      offsetStyle: styles.actionOffsetC,
      onPress: openCreateParentModal,
      requiresAuth: true,
    },
    {
      key: 'reports',
      label: 'סטטיסטיקות',
      icon: '📊',
      tintStyle: styles.actionTintFoam,
      offsetStyle: styles.actionOffsetD,
      onPress: () => navigation.navigate('ManagerSystemReports', route?.params || {}),
      requiresAuth: true,
    },
    {
      key: 'attendance',
      label: 'דו"ח נוכחות',
      icon: '🗓️',
      tintStyle: styles.actionTintMint,
      offsetStyle: styles.actionOffsetE,
      onPress: () => navigation.navigate('ManagerAttendanceReport', route?.params || {}),
      requiresAuth: true,
    },
    {
      key: 'create-group',
      label: 'יצירת קבוצה',
      icon: '➕',
      tintStyle: styles.actionTintCoral,
      offsetStyle: styles.actionOffsetG,
      onPress: openCreateGroupModal,
      requiresAuth: true,
    },
  ];

  const handleActionPress = (actionItem) => {
    if (actionItem.requiresAuth && !ensureManagerAuth('להיכנס למסך ניהול')) {
      return;
    }

    actionItem.onPress();
  };

  return (
    <View style={styles.safeArea} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />

      <LoginAquaticBackground />

      <View style={styles.topHeaderRow}>
        <View style={styles.topHeaderTextWrap}>
          <Text style={styles.topHeaderTitle}>פאנל מנהל</Text>
        </View>

        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
          <TouchableOpacity
            onPress={() => {
              if (!ensureManagerAuth('להיכנס לצ\'אט AI')) {
                return;
              }
              navigation.navigate('ChatPage', {
                fromInstructor: true,
                isAiChat: true,
                isManager: true,
                instructorId: Number(managerAuth?.id ?? 0),
                instructorName: String(managerAuth?.fullName ?? 'מנהל'),
              });
            }}
            style={[styles.logoutButton, { backgroundColor: 'rgba(0, 212, 255, 0.2)', borderColor: 'rgba(0, 212, 255, 0.45)' }]}
            activeOpacity={0.85}
          >
            <Text style={[styles.logoutButtonText, { color: '#00d4ff' }]}>✨ AI צ'אט</Text>
          </TouchableOpacity>
          <TouchableOpacity style={styles.logoutButton} activeOpacity={0.85} onPress={handleDisconnect}>
            <Text style={styles.logoutButtonText}>התנתקות</Text>
          </TouchableOpacity>
        </View>
      </View>

      <ScrollView
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
      >
        <View style={styles.mainCard}>
          <Text style={styles.sectionTitle}>סטטיסטיקות מרכז</Text>

          <View style={styles.statsRow}>
            <StatCard number={groups.length.toString()} label="קבוצות פעילות" variant="cool" />
            <StatCard number={instructors.length.toString()} label="מדריכים זמינים" variant="warm" />
          </View>

          <Text style={[styles.sectionTitle, styles.groupsTitle]}>אפליקציות מנהל</Text>
          <Text style={styles.actionsSubtitle}>בחרו אייקון לפתיחה מהירה של פעולה</Text>

          {!hasManagerAuth ? (
            <Text style={styles.listStateText}>לא זוהתה התחברות מנהל פעילה. התחברו מחדש כמנהל.</Text>
          ) : isLoadingData ? (
            <Text style={styles.listStateText}>טוען נתוני מנהל...</Text>
          ) : null}

          <View style={styles.actionCloud}>
            {managerActionItems.map((actionItem) => {
              const isDisabled = actionItem.requiresAuth && !hasManagerAuth;

              return (
                <ManagerActionIcon
                  key={actionItem.key}
                  icon={actionItem.icon}
                  label={actionItem.label}
                  tintStyle={actionItem.tintStyle}
                  offsetStyle={actionItem.offsetStyle}
                  disabled={isDisabled}
                  onPress={() => handleActionPress(actionItem)}
                />
              );
            })}
          </View>

          {groups.length === 0 && hasManagerAuth && !isLoadingData ? (
            <Text style={styles.listStateText}>לא נמצאו עדיין קבוצות פעילות. אפשר ליצור קבוצה חדשה דרך האייקון.</Text>
          ) : null}

          <View style={styles.managementSectionWrap}>
            <Text style={styles.managementSectionTitle}>רשימת מדריכים</Text>
            <TextInput
              value={searchInstructorText}
              onChangeText={setSearchInstructorText}
              style={[styles.input, styles.searchInput]}
              placeholder="חיפוש מדריך לפי שם, אימייל או קבוצה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />

            {filteredInstructors.length === 0 ? (
              <Text style={styles.managementEmptyText}>לא נמצאו מדריכים.</Text>
            ) : (
              <ScrollView
                style={styles.managementListContainer}
                contentContainerStyle={styles.managementListContent}
                nestedScrollEnabled={true}
                showsVerticalScrollIndicator={true}
              >
                {filteredInstructors.map((instructor) => (
                  <View key={instructor.instructorId} style={styles.managementRow}>
                    <View style={styles.managementRowTextWrap}>
                      <Text style={styles.managementRowTitle}>{instructor.fullName || instructor.email}</Text>
                      <Text style={styles.managementRowMeta}>{instructor.email}</Text>
                      {Array.isArray(instructor.groupNames) && instructor.groupNames.length > 0 ? (
                        <Text style={styles.managementRowSubMeta}>קבוצות: {instructor.groupNames.join(', ')}</Text>
                      ) : null}
                    </View>

                    <View style={styles.managementRowActions}>
                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityEditButton}
                        onPress={() => openEditInstructorModal(instructor)}
                      >
                        <Text style={styles.entityEditButtonText}>עריכה</Text>
                      </TouchableOpacity>

                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityDeleteButton}
                        onPress={() => confirmDeleteInstructor(instructor)}
                      >
                        <Text style={styles.entityDeleteButtonText}>מחיקה</Text>
                      </TouchableOpacity>
                    </View>
                  </View>
                ))}
              </ScrollView>
            )}
          </View>

          <View style={styles.managementSectionWrap}>
            <Text style={styles.managementSectionTitle}>רשימת הורים</Text>
            <TextInput
              value={searchParentText}
              onChangeText={setSearchParentText}
              style={[styles.input, styles.searchInput]}
              placeholder="חיפוש הורה לפי שם, אימייל, טלפון או ילד"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />

            {filteredParents.length === 0 ? (
              <Text style={styles.managementEmptyText}>לא נמצאו הורים.</Text>
            ) : (
              <ScrollView
                style={styles.managementListContainer}
                contentContainerStyle={styles.managementListContent}
                nestedScrollEnabled={true}
                showsVerticalScrollIndicator={true}
              >
                {filteredParents.map((parent) => (
                  <View key={parent.parentId} style={styles.managementRow}>
                    <View style={styles.managementRowTextWrap}>
                      <Text style={styles.managementRowTitle}>{parent.fullName || parent.email}</Text>
                      <Text style={styles.managementRowMeta}>{parent.email}</Text>
                      {parent.phone ? (
                        <Text style={styles.managementRowSubMeta}>טלפון: {parent.phone}</Text>
                      ) : null}
                      {Array.isArray(parent.childNames) && parent.childNames.length > 0 ? (
                        <Text style={styles.managementRowSubMeta}>ילדים: {parent.childNames.join(', ')}</Text>
                      ) : null}
                    </View>

                    <View style={styles.managementRowActions}>
                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityEditButton}
                        onPress={() => openEditParentModal(parent)}
                      >
                        <Text style={styles.entityEditButtonText}>עריכה</Text>
                      </TouchableOpacity>

                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityDeleteButton}
                        onPress={() => confirmDeleteParent(parent)}
                      >
                        <Text style={styles.entityDeleteButtonText}>מחיקה</Text>
                      </TouchableOpacity>
                    </View>
                  </View>
                ))}
              </ScrollView>
            )}
          </View>

          <View style={styles.managementSectionWrap}>
            <Text style={styles.managementSectionTitle}>רשימת ילדים</Text>
            <TextInput
              value={searchChildText}
              onChangeText={setSearchChildText}
              style={[styles.input, styles.searchInput]}
              placeholder="חיפוש ילד לפי שם, הורה או תאריך לידה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />

            {filteredChildren.length === 0 ? (
              <Text style={styles.managementEmptyText}>לא נמצאו ילדים.</Text>
            ) : (
              <ScrollView
                style={styles.managementListContainer}
                contentContainerStyle={styles.managementListContent}
                nestedScrollEnabled={true}
                showsVerticalScrollIndicator={true}
              >
                {filteredChildren.map((child) => (
                  <View key={child.childId} style={styles.managementRow}>
                    <View style={styles.managementRowTextWrap}>
                      <Text style={styles.managementRowTitle}>{child.fullName}</Text>
                      <Text style={styles.managementRowMeta}>{child.parentDisplayLabel}</Text>
                      {child.activeGroupName ? (
                        <Text style={styles.managementRowSubMeta}>קבוצה: {child.activeGroupName}</Text>
                      ) : null}
                      {child.activeGroupName ? (
                        <Text style={styles.managementRowSubMeta}>
                          מדריך: {child.assignedInstructorDisplay || 'לא משויך'}
                        </Text>
                      ) : null}
                      {child.birthDate ? (
                        <Text style={styles.managementRowSubMeta}>תאריך לידה: {child.birthDate}</Text>
                      ) : null}
                      {child.personalGoals && child.personalGoals.length > 0 ? (
                        <View style={styles.personalGoalsSection}>
                          <Text style={[styles.managementRowSubMeta, { fontWeight: '700', marginBottom: 6 }]}>יעדים אישיים:</Text>
                          {(typeof child.personalGoals === 'string' ? JSON.parse(child.personalGoals) : child.personalGoals).map((goal, idx) => (
                            <View key={`goal-${child.childId}-${idx}`} style={styles.personalGoalItem}>
                              <Text style={styles.personalGoalIcon}>{goal.type === 'weakness' ? '🎯' : '⭐'}</Text>
                              <View style={styles.personalGoalTextWrap}>
                                <Text style={[
                                  styles.personalGoalType,
                                  goal.type === 'weakness' ? styles.personalGoalTypeWeakness : styles.personalGoalTypeStrength,
                                ]}>
                                  {goal.areaLabel} — {goal.typeLabel}
                                </Text>
                                <Text style={styles.personalGoalText}>{goal.goal}</Text>
                              </View>
                            </View>
                          ))}
                        </View>
                      ) : null}
                    </View>

                    <View style={styles.managementRowActions}>
                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityEditButton}
                        onPress={() => openEditChildModal(child)}
                      >
                        <Text style={styles.entityEditButtonText}>עריכה</Text>
                      </TouchableOpacity>

                      <TouchableOpacity
                        activeOpacity={0.85}
                        style={styles.entityDeleteButton}
                        onPress={() => confirmDeleteChild(child)}
                      >
                        <Text style={styles.entityDeleteButtonText}>מחיקה</Text>
                      </TouchableOpacity>
                    </View>
                  </View>
                ))}
              </ScrollView>
            )}
          </View>

          <View style={styles.managementSectionWrap}>
            <Text style={styles.managementSectionTitle}>רשימת קבוצות</Text>
            <TextInput
              value={searchGroupText}
              onChangeText={setSearchGroupText}
              style={[styles.input, styles.searchInput]}
              placeholder="חיפוש קבוצה לפי שם הקבוצה או שם המדריך"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />

            {filteredGroups.length === 0 ? (
              <Text style={styles.managementEmptyText}>לא נמצאו קבוצות.</Text>
            ) : (
              <ScrollView
                style={styles.managementListContainer}
                contentContainerStyle={styles.managementListContent}
                nestedScrollEnabled={true}
                showsVerticalScrollIndicator={true}
              >
                {filteredGroups.map((group) => {
                  const groupChildNames = childNamesByGroupId.get(group.groupId) || [];
                  const isChildrenExpanded = expandedGroupChildrenId === group.groupId;

                  return (
                    <View key={group.groupId} style={styles.managementRow}>
                      <View style={styles.managementRowTextWrap}>
                        <Text style={styles.managementRowTitle}>{group.name}</Text>
                        <Text style={styles.managementRowMeta}>
                          מדריך: {group.instructorEmail && group.instructorFullName
                            ? `${group.instructorEmail} - ${group.instructorFullName}`
                            : group.instructorEmail || group.instructorFullName || 'לא משויך'}
                        </Text>
                        <Text style={styles.managementRowSubMeta}>ילדים פעילים: {group.activeChildrenCount}</Text>
                        {group.description ? (
                          <Text style={styles.managementRowSubMeta}>תיאור: {group.description}</Text>
                        ) : null}

                        <TouchableOpacity
                          activeOpacity={0.85}
                          style={styles.groupChildrenToggle}
                          onPress={() => setExpandedGroupChildrenId((prevGroupId) => (
                            prevGroupId === group.groupId ? null : group.groupId
                          ))}
                        >
                          <Text style={styles.groupChildrenToggleText}>
                            {isChildrenExpanded
                              ? 'הסתר ילדים ▲'
                              : `הצג ילדים (${groupChildNames.length}) ▼`}
                          </Text>
                        </TouchableOpacity>

                        {isChildrenExpanded ? (
                          <View style={styles.groupChildrenDropdownPanel}>
                            {groupChildNames.length === 0 ? (
                              <Text style={styles.groupChildrenEmptyText}>אין ילדים משויכים לקבוצה זו.</Text>
                            ) : groupChildNames.map((childName) => (
                              <Text key={`${group.groupId}-${childName}`} style={styles.groupChildrenName}>
                                • {childName}
                              </Text>
                            ))}
                          </View>
                        ) : null}
                      </View>

                      <View style={styles.managementRowActions}>
                        <TouchableOpacity
                          activeOpacity={0.85}
                          style={styles.entityEditButton}
                          onPress={() => openEditGroupModal(group)}
                        >
                          <Text style={styles.entityEditButtonText}>עריכה</Text>
                        </TouchableOpacity>

                        <TouchableOpacity
                          activeOpacity={0.85}
                          style={styles.entityDeleteButton}
                          onPress={() => confirmDeleteGroup(group)}
                        >
                          <Text style={styles.entityDeleteButtonText}>מחיקה</Text>
                        </TouchableOpacity>
                      </View>
                    </View>
                  );
                })}
              </ScrollView>
            )}
          </View>

          {!hasManagerAuth ? (
            <Text style={styles.listStateText}>הפעולות מושבתות עד התחברות מנהל מחדש.</Text>
          ) : null}
        </View>
      </ScrollView>

      <Modal
        visible={showCreateGroupModal}
        transparent
        animationType="fade"
        onRequestClose={closeCreateGroupModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>יצירת קבוצה חדשה</Text>
            <Text style={styles.modalSubtitle}>הקבוצה החדשה תקבל מדריך יחיד אחד.</Text>

            <Text style={styles.fieldLabel}>שם קבוצה</Text>
            <TextInput
              value={newGroupName}
              onChangeText={(value) => {
                setNewGroupName(value);
                if (createGroupErrors.name && value.trim()) {
                  setCreateGroupErrors((prev) => ({ ...prev, name: '' }));
                }
              }}
              style={[styles.input, createGroupErrors.name ? styles.inputError : null]}
              placeholder="לדוגמה: קבוצת דולפינים"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createGroupErrors.name ? (
              <Text style={styles.fieldErrorText}>{createGroupErrors.name}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>תיאור</Text>
            <TextInput
              value={newGroupDescription}
              onChangeText={(value) => {
                setNewGroupDescription(value);
                if (createGroupErrors.description && value.trim()) {
                  setCreateGroupErrors((prev) => ({ ...prev, description: '' }));
                }
              }}
              style={[styles.input, createGroupErrors.description ? styles.inputError : null]}
              placeholder="לדוגמה: הקבוצה מתמחה בוויסות רגשי לילד ובתנועות מוטוריות"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createGroupErrors.description ? (
              <Text style={styles.fieldErrorText}>{createGroupErrors.description}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>מדריך לקבוצה</Text>
            <TouchableOpacity
              style={[
                styles.dropdownTrigger,
                !hasInstructors ? styles.dropdownTriggerDisabled : null,
                createGroupErrors.instructor ? styles.dropdownTriggerError : null,
              ]}
              activeOpacity={hasInstructors ? 0.85 : 1}
              onPress={() => {
                if (!hasInstructors) {
                  return;
                }

                setIsCreateInstructorDropdownOpen((prev) => !prev);
              }}
              disabled={!hasInstructors}
            >
              <Text style={[styles.dropdownText, !selectedInstructor ? styles.dropdownPlaceholderText : null]}>
                {selectedInstructor
                  ? `${selectedInstructor.fullName} (${selectedInstructor.email})`
                  : hasInstructors
                    ? 'בחרו מדריך'
                    : 'אין מדריכים זמינים'}
              </Text>
              <Text style={styles.dropdownArrow}>{isCreateInstructorDropdownOpen ? '▲' : '▼'}</Text>
            </TouchableOpacity>
            {createGroupErrors.instructor ? (
              <Text style={styles.fieldErrorText}>{createGroupErrors.instructor}</Text>
            ) : null}

            {!hasInstructors ? (
              <Text style={styles.fieldHintText}>לא ניתן לבחור מדריך כי לא קיימים מדריכים פעילים במערכת.</Text>
            ) : null}

            {isCreateInstructorDropdownOpen ? (
              <View style={styles.dropdownPanel}>
                <ScrollView style={styles.dropdownScroll} nestedScrollEnabled keyboardShouldPersistTaps="handled">
                  {instructors.map((instructor) => (
                    <TouchableOpacity
                      key={instructor.instructorId}
                      style={[
                        styles.dropdownOption,
                        selectedInstructorId === instructor.instructorId ? styles.dropdownOptionActive : null,
                      ]}
                      activeOpacity={0.8}
                      onPress={() => {
                        setSelectedInstructorId(instructor.instructorId);
                        setIsCreateInstructorDropdownOpen(false);
                        if (createGroupErrors.instructor) {
                          setCreateGroupErrors((prev) => ({ ...prev, instructor: '' }));
                        }
                      }}
                    >
                      <Text
                        style={[
                          styles.dropdownOptionText,
                          selectedInstructorId === instructor.instructorId ? styles.dropdownOptionTextActive : null,
                        ]}
                      >
                        {instructor.fullName}
                      </Text>
                      <Text style={styles.dropdownOptionMeta}>{instructor.email}</Text>
                    </TouchableOpacity>
                  ))}
                </ScrollView>
              </View>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isSavingGroup ? 'שומר...' : hasInstructors ? 'יצירת קבוצה' : 'אין מדריכים זמינים'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#1F8D7A', '#1A7465']}
                onPress={createGroup}
                disabled={isSavingGroup || !hasInstructors}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeCreateGroupModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showCreateInstructorModal}
        transparent
        animationType="fade"
        onRequestClose={closeCreateInstructorModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>יצירת מדריך חדש</Text>
            <Text style={styles.modalSubtitle}>יש למלא את כל השדות כדי ליצור חשבון מדריך.</Text>

            <Text style={styles.fieldLabel}>שם פרטי</Text>
            <TextInput
              value={newInstructorFirstName}
              onChangeText={(value) => {
                setNewInstructorFirstName(value);
                if (createInstructorErrors.firstName && value.trim()) {
                  setCreateInstructorErrors((prev) => ({ ...prev, firstName: '' }));
                }
              }}
              style={[styles.input, createInstructorErrors.firstName ? styles.inputError : null]}
              placeholder="שם פרטי"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createInstructorErrors.firstName ? (
              <Text style={styles.fieldErrorText}>{createInstructorErrors.firstName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>שם משפחה</Text>
            <TextInput
              value={newInstructorLastName}
              onChangeText={(value) => {
                setNewInstructorLastName(value);
                if (createInstructorErrors.lastName && value.trim()) {
                  setCreateInstructorErrors((prev) => ({ ...prev, lastName: '' }));
                }
              }}
              style={[styles.input, createInstructorErrors.lastName ? styles.inputError : null]}
              placeholder="שם משפחה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createInstructorErrors.lastName ? (
              <Text style={styles.fieldErrorText}>{createInstructorErrors.lastName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>אימייל</Text>
            <TextInput
              value={newInstructorEmail}
              onChangeText={(value) => {
                setNewInstructorEmail(value);
                if (createInstructorErrors.email && value.trim()) {
                  setCreateInstructorErrors((prev) => ({ ...prev, email: '' }));
                }
              }}
              style={[styles.input, createInstructorErrors.email ? styles.inputError : null]}
              placeholder="example@email.com"
              placeholderTextColor="#98A3AD"
              autoCapitalize="none"
              keyboardType="email-address"
              textAlign="right"
            />
            {createInstructorErrors.email ? (
              <Text style={styles.fieldErrorText}>{createInstructorErrors.email}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>סיסמה</Text>
            <TextInput
              value={newInstructorPassword}
              onChangeText={(value) => {
                setNewInstructorPassword(value);
                if (createInstructorErrors.password && value.length >= 6) {
                  setCreateInstructorErrors((prev) => ({ ...prev, password: '' }));
                }
              }}
              style={[styles.input, createInstructorErrors.password ? styles.inputError : null]}
              placeholder="לפחות 6 תווים"
              placeholderTextColor="#98A3AD"
              secureTextEntry
              textAlign="right"
            />
            {createInstructorErrors.password ? (
              <Text style={styles.fieldErrorText}>{createInstructorErrors.password}</Text>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isSavingInstructor ? 'שומר...' : 'יצירת מדריך'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#2E77BC', '#255E97']}
                onPress={createInstructor}
                disabled={isSavingInstructor}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeCreateInstructorModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showCreateParentModal}
        transparent
        animationType="fade"
        onRequestClose={closeCreateParentModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>יצירת הורה חדש</Text>
            <Text style={styles.modalSubtitle}>יש למלא שם, אימייל וסיסמה כדי ליצור חשבון הורה.</Text>

            <Text style={styles.fieldLabel}>שם פרטי</Text>
            <TextInput
              value={newParentFirstName}
              onChangeText={(value) => {
                setNewParentFirstName(value);
                if (createParentErrors.firstName && value.trim()) {
                  setCreateParentErrors((prev) => ({ ...prev, firstName: '' }));
                }
              }}
              style={[styles.input, createParentErrors.firstName ? styles.inputError : null]}
              placeholder="שם פרטי"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createParentErrors.firstName ? (
              <Text style={styles.fieldErrorText}>{createParentErrors.firstName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>שם משפחה</Text>
            <TextInput
              value={newParentLastName}
              onChangeText={(value) => {
                setNewParentLastName(value);
                if (createParentErrors.lastName && value.trim()) {
                  setCreateParentErrors((prev) => ({ ...prev, lastName: '' }));
                }
              }}
              style={[styles.input, createParentErrors.lastName ? styles.inputError : null]}
              placeholder="שם משפחה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {createParentErrors.lastName ? (
              <Text style={styles.fieldErrorText}>{createParentErrors.lastName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>אימייל</Text>
            <TextInput
              value={newParentEmail}
              onChangeText={(value) => {
                setNewParentEmail(value);
                if (createParentErrors.email && value.trim()) {
                  setCreateParentErrors((prev) => ({ ...prev, email: '' }));
                }
              }}
              style={[styles.input, createParentErrors.email ? styles.inputError : null]}
              placeholder="example@email.com"
              placeholderTextColor="#98A3AD"
              autoCapitalize="none"
              keyboardType="email-address"
              textAlign="right"
            />
            {createParentErrors.email ? (
              <Text style={styles.fieldErrorText}>{createParentErrors.email}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>סיסמה</Text>
            <TextInput
              value={newParentPassword}
              onChangeText={(value) => {
                setNewParentPassword(value);
                if (createParentErrors.password && value.length >= 6) {
                  setCreateParentErrors((prev) => ({ ...prev, password: '' }));
                }
              }}
              style={[styles.input, createParentErrors.password ? styles.inputError : null]}
              placeholder="לפחות 6 תווים"
              placeholderTextColor="#98A3AD"
              secureTextEntry
              textAlign="right"
            />
            {createParentErrors.password ? (
              <Text style={styles.fieldErrorText}>{createParentErrors.password}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>טלפון (אופציונלי)</Text>
            <TextInput
              value={newParentPhone}
              onChangeText={(value) => {
                setNewParentPhone(value);
                if (createParentErrors.phone) {
                  setCreateParentErrors((prev) => ({ ...prev, phone: '' }));
                }
              }}
              style={[styles.input, createParentErrors.phone ? styles.inputError : null]}
              placeholder="050-0000000"
              placeholderTextColor="#98A3AD"
              keyboardType="phone-pad"
              textAlign="right"
            />
            {createParentErrors.phone ? (
              <Text style={styles.fieldErrorText}>{createParentErrors.phone}</Text>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isSavingParent ? 'שומר...' : 'יצירת הורה'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#2E77BC', '#255E97']}
                onPress={createParent}
                disabled={isSavingParent}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeCreateParentModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showCreateChildModal}
        transparent
        animationType="fade"
        onRequestClose={closeCreateChildModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator nestedScrollEnabled>
              <Text style={styles.modalTitle}>יצירת ילד חדש</Text>
              <Text style={styles.modalSubtitle}>יש למלא שם פרטי, שם משפחה ולבחור הורה.</Text>

              <Text style={styles.fieldLabel}>שם פרטי</Text>
              <TextInput
                value={newChildFirstName}
                onChangeText={(value) => {
                  setNewChildFirstName(value);
                  if (createChildErrors.firstName && value.trim()) {
                    setCreateChildErrors((prev) => ({ ...prev, firstName: '' }));
                  }
                }}
                style={[styles.input, createChildErrors.firstName ? styles.inputError : null]}
                placeholder="שם פרטי"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {createChildErrors.firstName ? (
                <Text style={styles.fieldErrorText}>{createChildErrors.firstName}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>שם משפחה</Text>
              <TextInput
                value={newChildLastName}
                onChangeText={(value) => {
                  setNewChildLastName(value);
                  if (createChildErrors.lastName && value.trim()) {
                    setCreateChildErrors((prev) => ({ ...prev, lastName: '' }));
                  }
                }}
                style={[styles.input, createChildErrors.lastName ? styles.inputError : null]}
                placeholder="שם משפחה"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {createChildErrors.lastName ? (
                <Text style={styles.fieldErrorText}>{createChildErrors.lastName}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>תאריך לידה (YYYY-MM-DD, אופציונלי)</Text>
              <TextInput
                value={newChildBirthDate}
                onChangeText={(value) => {
                  setNewChildBirthDate(value);
                  if (createChildErrors.birthDate) {
                    setCreateChildErrors((prev) => ({ ...prev, birthDate: '' }));
                  }
                }}
                style={[styles.input, createChildErrors.birthDate ? styles.inputError : null]}
                placeholder="2018-05-20"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {createChildErrors.birthDate ? (
                <Text style={styles.fieldErrorText}>{createChildErrors.birthDate}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>תיאור מקצועי עבור המלצת AI</Text>
              <TextInput
                value={newChildDescription}
                onChangeText={(value) => {
                  setNewChildDescription(value);
                  if (createChildErrors.childDescription && value.trim()) {
                    setCreateChildErrors((prev) => ({ ...prev, childDescription: '' }));
                  }
                }}
                style={[styles.input, createChildErrors.childDescription ? styles.inputError : null]}
                placeholder="לדוגמה: הילד זקוק לחיזוק ביטחון במים, וויסות רגשי ויציבות מוטורית"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {createChildErrors.childDescription ? (
                <Text style={styles.fieldErrorText}>{createChildErrors.childDescription}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>חוזקות (בחירה מרובה)</Text>
              <View style={styles.skillChipsContainer}>
                {SWIMMING_SKILL_AREAS.map((skill) => {
                  const isSelected = newChildStrengths.includes(skill.key);
                  const isDisabledByWeakness = newChildWeaknesses.includes(skill.key);
                  return (
                    <TouchableOpacity
                      key={`str-${skill.key}`}
                      activeOpacity={0.75}
                      style={[
                        styles.skillChip,
                        isSelected ? styles.skillChipStrengthActive : null,
                        isDisabledByWeakness ? styles.skillChipDisabled : null,
                      ]}
                      disabled={isDisabledByWeakness}
                      onPress={() => {
                        setNewChildStrengths((prev) =>
                          prev.includes(skill.key) ? prev.filter((k) => k !== skill.key) : [...prev, skill.key]
                        );
                      }}
                    >
                      <Text style={[
                        styles.skillChipText,
                        isSelected ? styles.skillChipTextActive : null,
                        isDisabledByWeakness ? styles.skillChipTextDisabled : null,
                      ]}>
                        {isSelected ? '✓ ' : ''}{skill.label}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <Text style={styles.fieldLabel}>חולשות (בחירה מרובה)</Text>
              <View style={styles.skillChipsContainer}>
                {SWIMMING_SKILL_AREAS.map((skill) => {
                  const isSelected = newChildWeaknesses.includes(skill.key);
                  const isDisabledByStrength = newChildStrengths.includes(skill.key);
                  return (
                    <TouchableOpacity
                      key={`wk-${skill.key}`}
                      activeOpacity={0.75}
                      style={[
                        styles.skillChip,
                        isSelected ? styles.skillChipWeaknessActive : null,
                        isDisabledByStrength ? styles.skillChipDisabled : null,
                      ]}
                      disabled={isDisabledByStrength}
                      onPress={() => {
                        setNewChildWeaknesses((prev) =>
                          prev.includes(skill.key) ? prev.filter((k) => k !== skill.key) : [...prev, skill.key]
                        );
                      }}
                    >
                      <Text style={[
                        styles.skillChipText,
                        isSelected ? styles.skillChipTextActive : null,
                        isDisabledByStrength ? styles.skillChipTextDisabled : null,
                      ]}>
                        {isSelected ? '✓ ' : ''}{skill.label}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <Text style={styles.fieldLabel}>הורה</Text>
              <TouchableOpacity
                style={[
                  styles.dropdownTrigger,
                  !hasParents ? styles.dropdownTriggerDisabled : null,
                  createChildErrors.parentId ? styles.dropdownTriggerError : null,
                ]}
                activeOpacity={hasParents ? 0.85 : 1}
                onPress={() => {
                  if (!hasParents) {
                    return;
                  }

                  setIsCreateChildParentDropdownOpen((prev) => !prev);
                }}
                disabled={!hasParents}
              >
                <Text style={[styles.dropdownText, !selectedChildParent ? styles.dropdownPlaceholderText : null]}>
                  {selectedChildParent
                    ? selectedChildParent.displayLabel
                    : hasParents
                      ? 'בחרו הורה'
                      : 'אין הורים זמינים'}
                </Text>
                <Text style={styles.dropdownArrow}>{isCreateChildParentDropdownOpen ? '▲' : '▼'}</Text>
              </TouchableOpacity>
              {createChildErrors.parentId ? (
                <Text style={styles.fieldErrorText}>{createChildErrors.parentId}</Text>
              ) : null}

              {!hasParents ? (
                <Text style={styles.fieldHintText}>לא ניתן לבחור הורה כי לא קיימים הורים פעילים במערכת.</Text>
              ) : null}

              {isCreateChildParentDropdownOpen ? (
                <View style={styles.dropdownPanel}>
                  <ScrollView style={styles.dropdownScroll} nestedScrollEnabled keyboardShouldPersistTaps="handled">
                    {parents.map((parent) => (
                      <TouchableOpacity
                        key={parent.parentId}
                        style={[
                          styles.dropdownOption,
                          newChildParentId === parent.parentId ? styles.dropdownOptionActive : null,
                        ]}
                        activeOpacity={0.8}
                        onPress={() => {
                          setNewChildParentId(parent.parentId);
                          setIsCreateChildParentDropdownOpen(false);
                          if (createChildErrors.parentId) {
                            setCreateChildErrors((prev) => ({ ...prev, parentId: '' }));
                          }
                        }}
                      >
                        <Text
                          style={[
                            styles.dropdownOptionText,
                            newChildParentId === parent.parentId ? styles.dropdownOptionTextActive : null,
                          ]}
                        >
                          {parent.displayLabel}
                        </Text>
                      </TouchableOpacity>
                    ))}
                  </ScrollView>
                </View>
              ) : null}

              <View style={styles.modalActionsWrap}>
                <PrimaryButton
                  label={isSavingChild ? 'שומר...' : hasParents ? 'יצירת ילד' : 'אין הורים זמינים'}
                  style={styles.modalPrimaryButtonShell}
                  gradientStyle={styles.modalPrimaryButton}
                  textStyle={styles.modalPrimaryButtonText}
                  colorsOverride={['#1F8D7A', '#1A7465']}
                  onPress={createChild}
                  disabled={isSavingChild || !hasParents}
                />

                <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeCreateChildModal}>
                  <Text style={styles.modalCancelButtonText}>ביטול</Text>
                </TouchableOpacity>
              </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showEditInstructorModal}
        transparent
        animationType="fade"
        onRequestClose={closeEditInstructorModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>עריכת מדריך</Text>
            <Text style={styles.modalSubtitle}>{editInstructorEmail || 'עדכון פרטי מדריך קיים'}</Text>

            <Text style={styles.fieldLabel}>שם פרטי</Text>
            <TextInput
              value={editInstructorFirstName}
              onChangeText={(value) => {
                setEditInstructorFirstName(value);
                if (editInstructorErrors.firstName && value.trim()) {
                  setEditInstructorErrors((prev) => ({ ...prev, firstName: '' }));
                }
              }}
              style={[styles.input, editInstructorErrors.firstName ? styles.inputError : null]}
              placeholder="שם פרטי"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editInstructorErrors.firstName ? (
              <Text style={styles.fieldErrorText}>{editInstructorErrors.firstName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>שם משפחה</Text>
            <TextInput
              value={editInstructorLastName}
              onChangeText={(value) => {
                setEditInstructorLastName(value);
                if (editInstructorErrors.lastName && value.trim()) {
                  setEditInstructorErrors((prev) => ({ ...prev, lastName: '' }));
                }
              }}
              style={[styles.input, editInstructorErrors.lastName ? styles.inputError : null]}
              placeholder="שם משפחה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editInstructorErrors.lastName ? (
              <Text style={styles.fieldErrorText}>{editInstructorErrors.lastName}</Text>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isUpdatingInstructor ? 'שומר...' : 'שמירת שינויים'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#2E77BC', '#255E97']}
                onPress={updateInstructor}
                disabled={isUpdatingInstructor}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeEditInstructorModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showEditParentModal}
        transparent
        animationType="fade"
        onRequestClose={closeEditParentModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>עריכת הורה</Text>
            <Text style={styles.modalSubtitle}>{editParentEmail || 'עדכון פרטי הורה קיים'}</Text>

            <Text style={styles.fieldLabel}>שם פרטי</Text>
            <TextInput
              value={editParentFirstName}
              onChangeText={(value) => {
                setEditParentFirstName(value);
                if (editParentErrors.firstName && value.trim()) {
                  setEditParentErrors((prev) => ({ ...prev, firstName: '' }));
                }
              }}
              style={[styles.input, editParentErrors.firstName ? styles.inputError : null]}
              placeholder="שם פרטי"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editParentErrors.firstName ? (
              <Text style={styles.fieldErrorText}>{editParentErrors.firstName}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>שם משפחה</Text>
            <TextInput
              value={editParentLastName}
              onChangeText={(value) => {
                setEditParentLastName(value);
                if (editParentErrors.lastName && value.trim()) {
                  setEditParentErrors((prev) => ({ ...prev, lastName: '' }));
                }
              }}
              style={[styles.input, editParentErrors.lastName ? styles.inputError : null]}
              placeholder="שם משפחה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editParentErrors.lastName ? (
              <Text style={styles.fieldErrorText}>{editParentErrors.lastName}</Text>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isUpdatingParent ? 'שומר...' : 'שמירת שינויים'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#2E77BC', '#255E97']}
                onPress={updateParent}
                disabled={isUpdatingParent}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeEditParentModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showEditChildModal}
        transparent
        animationType="fade"
        onRequestClose={closeEditChildModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator nestedScrollEnabled>
              <Text style={styles.modalTitle}>עריכת ילד</Text>
              <Text style={styles.modalSubtitle}>עדכון פרטי ילד קיים</Text>

              <Text style={styles.fieldLabel}>שם פרטי</Text>
              <TextInput
                value={editChildFirstName}
                onChangeText={(value) => {
                  setEditChildFirstName(value);
                  if (editChildErrors.firstName && value.trim()) {
                    setEditChildErrors((prev) => ({ ...prev, firstName: '' }));
                  }
                }}
                style={[styles.input, editChildErrors.firstName ? styles.inputError : null]}
                placeholder="שם פרטי"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {editChildErrors.firstName ? (
                <Text style={styles.fieldErrorText}>{editChildErrors.firstName}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>שם משפחה</Text>
              <TextInput
                value={editChildLastName}
                onChangeText={(value) => {
                  setEditChildLastName(value);
                  if (editChildErrors.lastName && value.trim()) {
                    setEditChildErrors((prev) => ({ ...prev, lastName: '' }));
                  }
                }}
                style={[styles.input, editChildErrors.lastName ? styles.inputError : null]}
                placeholder="שם משפחה"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {editChildErrors.lastName ? (
                <Text style={styles.fieldErrorText}>{editChildErrors.lastName}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>תאריך לידה (YYYY-MM-DD, אופציונלי)</Text>
              <TextInput
                value={editChildBirthDate}
                onChangeText={(value) => {
                  setEditChildBirthDate(value);
                  if (editChildErrors.birthDate) {
                    setEditChildErrors((prev) => ({ ...prev, birthDate: '' }));
                  }
                }}
                style={[styles.input, editChildErrors.birthDate ? styles.inputError : null]}
                placeholder="2018-05-20"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />
              {editChildErrors.birthDate ? (
                <Text style={styles.fieldErrorText}>{editChildErrors.birthDate}</Text>
              ) : null}

              <Text style={styles.fieldLabel}>תיאור מקצועי עבור המלצת AI</Text>
              <TextInput
                value={editChildDescription}
                onChangeText={(value) => {
                  setEditChildDescription(value);
                  if (editChildAiRecommendation) {
                    setEditChildAiRecommendation(null);
                  }
                }}
                style={[styles.input, styles.multilineInput]}
                placeholder="לדוגמה: הילד זקוק לחיזוק ביטחון במים, וויסות רגשי ויציבות מוטורית"
                placeholderTextColor="#98A3AD"
                textAlign="right"
                multiline
                textAlignVertical="top"
              />

              <Text style={styles.fieldLabel}>חוזקות (בחירה מרובה)</Text>
              <View style={styles.skillChipsContainer}>
                {SWIMMING_SKILL_AREAS.map((skill) => {
                  const isSelected = editChildStrengths.includes(skill.key);
                  const isDisabledByWeakness = editChildWeaknesses.includes(skill.key);
                  return (
                    <TouchableOpacity
                      key={`edit-str-${skill.key}`}
                      activeOpacity={0.75}
                      style={[
                        styles.skillChip,
                        isSelected ? styles.skillChipStrengthActive : null,
                        isDisabledByWeakness ? styles.skillChipDisabled : null,
                      ]}
                      disabled={isDisabledByWeakness}
                      onPress={() => {
                        setEditChildStrengths((prev) =>
                          prev.includes(skill.key) ? prev.filter((k) => k !== skill.key) : [...prev, skill.key]
                        );
                      }}
                    >
                      <Text style={[
                        styles.skillChipText,
                        isSelected ? styles.skillChipTextActive : null,
                        isDisabledByWeakness ? styles.skillChipTextDisabled : null,
                      ]}>
                        {isSelected ? '✓ ' : ''}{skill.label}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <Text style={styles.fieldLabel}>חולשות (בחירה מרובה)</Text>
              <View style={styles.skillChipsContainer}>
                {SWIMMING_SKILL_AREAS.map((skill) => {
                  const isSelected = editChildWeaknesses.includes(skill.key);
                  const isDisabledByStrength = editChildStrengths.includes(skill.key);
                  return (
                    <TouchableOpacity
                      key={`edit-wk-${skill.key}`}
                      activeOpacity={0.75}
                      style={[
                        styles.skillChip,
                        isSelected ? styles.skillChipWeaknessActive : null,
                        isDisabledByStrength ? styles.skillChipDisabled : null,
                      ]}
                      disabled={isDisabledByStrength}
                      onPress={() => {
                        setEditChildWeaknesses((prev) =>
                          prev.includes(skill.key) ? prev.filter((k) => k !== skill.key) : [...prev, skill.key]
                        );
                      }}
                    >
                      <Text style={[
                        styles.skillChipText,
                        isSelected ? styles.skillChipTextActive : null,
                        isDisabledByStrength ? styles.skillChipTextDisabled : null,
                      ]}>
                        {isSelected ? '✓ ' : ''}{skill.label}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <Text style={styles.fieldLabel}>הורה</Text>
              <TouchableOpacity
                style={[
                  styles.dropdownTrigger,
                  !hasParents ? styles.dropdownTriggerDisabled : null,
                  editChildErrors.parentId ? styles.dropdownTriggerError : null,
                ]}
                activeOpacity={hasParents ? 0.85 : 1}
                onPress={() => {
                  if (!hasParents) {
                    return;
                  }

                  setIsEditChildParentDropdownOpen((prev) => !prev);
                  setIsEditChildGroupDropdownOpen(false);
                }}
                disabled={!hasParents}
              >
                <Text style={[styles.dropdownText, !selectedEditChildParent ? styles.dropdownPlaceholderText : null]}>
                  {selectedEditChildParent
                    ? selectedEditChildParent.displayLabel
                    : hasParents
                      ? 'בחרו הורה'
                      : 'אין הורים זמינים'}
                </Text>
                <Text style={styles.dropdownArrow}>{isEditChildParentDropdownOpen ? '▲' : '▼'}</Text>
              </TouchableOpacity>
              {editChildErrors.parentId ? (
                <Text style={styles.fieldErrorText}>{editChildErrors.parentId}</Text>
              ) : null}

              {isEditChildParentDropdownOpen ? (
                <View style={styles.dropdownPanel}>
                  <ScrollView style={styles.dropdownScroll} nestedScrollEnabled keyboardShouldPersistTaps="handled">
                    {parents.map((parent) => (
                      <TouchableOpacity
                        key={parent.parentId}
                        style={[
                          styles.dropdownOption,
                          editChildParentId === parent.parentId ? styles.dropdownOptionActive : null,
                        ]}
                        activeOpacity={0.8}
                        onPress={() => {
                          setEditChildParentId(parent.parentId);
                          setIsEditChildParentDropdownOpen(false);
                          if (editChildErrors.parentId) {
                            setEditChildErrors((prev) => ({ ...prev, parentId: '' }));
                          }
                        }}
                      >
                        <Text
                          style={[
                            styles.dropdownOptionText,
                            editChildParentId === parent.parentId ? styles.dropdownOptionTextActive : null,
                          ]}
                        >
                          {parent.displayLabel}
                        </Text>
                      </TouchableOpacity>
                    ))}
                  </ScrollView>
                </View>
              ) : null}

              <Text style={styles.fieldLabel}>חיפוש קבוצה</Text>
              <TextInput
                value={searchEditChildGroupText}
                onChangeText={setSearchEditChildGroupText}
                style={[styles.input, styles.searchInput]}
                placeholder="חיפוש לפי שם קבוצה, תיאור או מדריך"
                placeholderTextColor="#98A3AD"
                textAlign="right"
              />

              <Text style={styles.fieldLabel}>קבוצה</Text>
              <TouchableOpacity
                style={styles.dropdownTrigger}
                activeOpacity={0.85}
                onPress={() => {
                  setIsEditChildGroupDropdownOpen((prev) => !prev);
                  setIsEditChildParentDropdownOpen(false);
                }}
              >
                <Text style={styles.dropdownText}>
                  {editChildGroupId === null
                    ? 'לא משוייך לקבוצה כרגע'
                    : selectedEditChildGroup?.name || `קבוצה ${editChildGroupId}`}
                </Text>
                <Text style={styles.dropdownArrow}>{isEditChildGroupDropdownOpen ? '▲' : '▼'}</Text>
              </TouchableOpacity>

              {isEditChildGroupDropdownOpen ? (
                <View style={styles.dropdownPanel}>
                  <ScrollView style={styles.dropdownScroll} nestedScrollEnabled keyboardShouldPersistTaps="handled">
                    <TouchableOpacity
                      style={[
                        styles.dropdownOption,
                        editChildGroupId === null ? styles.dropdownOptionActive : null,
                      ]}
                      activeOpacity={0.8}
                      onPress={selectNoGroupForChildEdit}
                    >
                      <Text
                        style={[
                          styles.dropdownOptionText,
                          editChildGroupId === null ? styles.dropdownOptionTextActive : null,
                        ]}
                      >
                        לא משוייך לקבוצה כרגע
                      </Text>
                      <Text style={styles.dropdownOptionMeta}>
                        {originalEditChildGroupId
                          ? 'הסרת שיוך מהקבוצה הנוכחית'
                          : 'הילד כבר ללא שיוך לקבוצה'}
                      </Text>
                    </TouchableOpacity>

                    {filteredGroupsForChildEdit.length === 0 ? (
                      <Text style={styles.managementEmptyText}>לא נמצאו קבוצות התואמות לחיפוש.</Text>
                    ) : filteredGroupsForChildEdit.map((group) => {
                      const isCurrentGroup = Boolean(
                        originalEditChildGroupId && group.groupId === originalEditChildGroupId
                      );

                      return (
                        <TouchableOpacity
                          key={group.groupId}
                          style={[
                            styles.dropdownOption,
                            editChildGroupId === group.groupId && !isCurrentGroup ? styles.dropdownOptionActive : null,
                            isCurrentGroup ? styles.dropdownOptionDisabled : null,
                          ]}
                          activeOpacity={isCurrentGroup ? 1 : 0.8}
                          onPress={() => {
                            if (isCurrentGroup) {
                              return;
                            }

                            selectGroupForChildEdit(group);
                          }}
                        >
                          <Text
                            style={[
                              styles.dropdownOptionText,
                              editChildGroupId === group.groupId && !isCurrentGroup ? styles.dropdownOptionTextActive : null,
                              isCurrentGroup ? styles.dropdownOptionTextDisabled : null,
                            ]}
                          >
                            {group.name}
                          </Text>
                          <Text style={[styles.dropdownOptionMeta, isCurrentGroup ? styles.dropdownOptionMetaDisabled : null]}>
                            {isCurrentGroup
                              ? 'הקבוצה הנוכחית של הילד (לא ניתנת לבחירה)'
                              : group.instructorFullName
                                ? `מדריך: ${group.instructorFullName}`
                                : 'קבוצה פעילה'}
                          </Text>
                        </TouchableOpacity>
                      );
                    })}
                  </ScrollView>
                </View>
              ) : null}

              <Text style={styles.aiSectionTitle}>מציאת קבוצה באמצעות בינה מלאכותית</Text>
              <Text style={styles.fieldHintText}>המערכת תשווה את תיאור הילד לתיאורי הקבוצות ותסביר את סיבת ההתאמה.</Text>

              <PrimaryButton
                label={isFindingAiGroupRecommendation ? 'מחפש התאמה...' : 'התאמת קבוצה בעזרת AI'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#1F8D7A', '#1A7465']}
                onPress={findAiGroupRecommendationForChildEdit}
                disabled={isFindingAiGroupRecommendation || isApplyingAiRecommendedGroup}
              />

              {editChildAiRecommendation ? (
                <View style={styles.aiRecommendationCard}>
                  <Text style={styles.aiRecommendationTitle}>
                    קבוצה מומלצת: {editChildAiRecommendation.recommendedGroupName || `קבוצה ${editChildAiRecommendation.recommendedGroupId}`}
                  </Text>

                  {editChildAiRecommendation.recommendedInstructorFullName ? (
                    <Text style={styles.aiRecommendationMeta}>מדריך: {editChildAiRecommendation.recommendedInstructorFullName}</Text>
                  ) : null}

                  {editChildAiRecommendation.usedFallback ? (
                    <Text style={styles.aiRecommendationFallback}>המלצה חלופית: מבוססת אלגוריתם התאמה פנימי.</Text>
                  ) : null}

                  <Text style={styles.aiRecommendationReasoning}>{editChildAiRecommendation.reasoning || 'לא התקבל נימוק מפורט.'}</Text>

                  <PrimaryButton
                    label={isApplyingAiRecommendedGroup ? 'מוסיף...' : 'הוסף את הילד לקבוצה זו'}
                    style={styles.aiApplyButtonShell}
                    gradientStyle={styles.aiApplyButton}
                    textStyle={styles.aiApplyButtonText}
                    colorsOverride={['#2E77BC', '#255E97']}
                    onPress={applyAiRecommendedGroupForChildEdit}
                    disabled={isApplyingAiRecommendedGroup || isFindingAiGroupRecommendation}
                  />
                </View>
              ) : null}

              <View style={styles.modalActionsWrap}>
                <PrimaryButton
                  label={isUpdatingChild ? 'שומר...' : 'שמירת שינויים'}
                  style={styles.modalPrimaryButtonShell}
                  gradientStyle={styles.modalPrimaryButton}
                  textStyle={styles.modalPrimaryButtonText}
                  colorsOverride={['#2E77BC', '#255E97']}
                  onPress={updateChild}
                  disabled={isUpdatingChild}
                />

                <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeEditChildModal}>
                  <Text style={styles.modalCancelButtonText}>ביטול</Text>
                </TouchableOpacity>
              </View>
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal
        visible={showEditGroupModal}
        transparent
        animationType="fade"
        onRequestClose={closeEditGroupModal}
      >
        <View style={styles.modalBackdrop} onStartShouldSetResponderCapture={dismissKeyboardOnTouchCapture}>
          <View style={styles.modalCard}>
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} nestedScrollEnabled>
              <Text style={styles.modalTitle}>עריכת קבוצה</Text>
            <Text style={styles.modalSubtitle}>עדכון שם קבוצה, מדריך ותיאור. כל השדות חובה.</Text>

            <Text style={styles.fieldLabel}>שם הקבוצה</Text>
            <TextInput
              value={editGroupName}
              onChangeText={(value) => {
                setEditGroupName(value);
                if (editGroupErrors.name && value.trim()) {
                  setEditGroupErrors((prev) => ({ ...prev, name: '' }));
                }
              }}
              style={[styles.input, editGroupErrors.name ? styles.inputError : null]}
              placeholder="שם קבוצה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editGroupErrors.name ? (
              <Text style={styles.fieldErrorText}>{editGroupErrors.name}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>תיאור</Text>
            <TextInput
              value={editGroupDescription}
              onChangeText={(value) => {
                setEditGroupDescription(value);
                if (editGroupErrors.description && value.trim()) {
                  setEditGroupErrors((prev) => ({ ...prev, description: '' }));
                }
              }}
              style={[styles.input, editGroupErrors.description ? styles.inputError : null]}
              placeholder="תיאור קבוצה"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />
            {editGroupErrors.description ? (
              <Text style={styles.fieldErrorText}>{editGroupErrors.description}</Text>
            ) : null}

            <Text style={styles.fieldLabel}>חיפוש מדריך</Text>
            <TextInput
              value={searchEditGroupInstructorText}
              onChangeText={setSearchEditGroupInstructorText}
              style={[styles.input, styles.searchInput]}
              placeholder="חיפוש לפי שם או אימייל מדריך"
              placeholderTextColor="#98A3AD"
              textAlign="right"
            />

            <Text style={styles.fieldLabel}>שם המדריך</Text>
            <TouchableOpacity
              style={[
                styles.dropdownTrigger,
                editGroupErrors.instructor ? styles.dropdownTriggerError : null,
              ]}
              activeOpacity={0.85}
              onPress={() => setIsEditGroupInstructorDropdownOpen((prev) => !prev)}
            >
              <Text style={[styles.dropdownText, !selectedEditGroupInstructor ? styles.dropdownPlaceholderText : null]}>
                {selectedEditGroupInstructor
                  ? `${selectedEditGroupInstructor.fullName} (${selectedEditGroupInstructor.email})`
                  : 'בחרו מדריך'}
              </Text>
              <Text style={styles.dropdownArrow}>{isEditGroupInstructorDropdownOpen ? '▲' : '▼'}</Text>
            </TouchableOpacity>
            {editGroupErrors.instructor ? (
              <Text style={styles.fieldErrorText}>{editGroupErrors.instructor}</Text>
            ) : null}

            {isEditGroupInstructorDropdownOpen ? (
              <View style={styles.dropdownPanel}>
                <ScrollView style={styles.dropdownScroll} nestedScrollEnabled keyboardShouldPersistTaps="handled">
                  {filteredInstructorsForGroupEdit.length === 0 ? (
                    <Text style={styles.managementEmptyText}>לא נמצאו מדריכים התואמים לחיפוש.</Text>
                  ) : filteredInstructorsForGroupEdit.map((instructor) => {
                    const isCurrentInstructor = Boolean(
                      originalEditGroupInstructorId && instructor.instructorId === originalEditGroupInstructorId
                    );

                    return (
                      <TouchableOpacity
                        key={instructor.instructorId}
                        style={[
                          styles.dropdownOption,
                          editGroupInstructorId === instructor.instructorId && !isCurrentInstructor
                            ? styles.dropdownOptionActive
                            : null,
                          isCurrentInstructor ? styles.dropdownOptionDisabled : null,
                        ]}
                        activeOpacity={isCurrentInstructor ? 1 : 0.8}
                        onPress={() => {
                          if (isCurrentInstructor) {
                            return;
                          }

                          selectInstructorForGroupEdit(instructor);
                        }}
                      >
                        <Text
                          style={[
                            styles.dropdownOptionText,
                            editGroupInstructorId === instructor.instructorId && !isCurrentInstructor
                              ? styles.dropdownOptionTextActive
                              : null,
                            isCurrentInstructor ? styles.dropdownOptionTextDisabled : null,
                          ]}
                        >
                          {instructor.fullName}
                        </Text>
                        <Text style={[styles.dropdownOptionMeta, isCurrentInstructor ? styles.dropdownOptionMetaDisabled : null]}>
                          {isCurrentInstructor
                            ? 'המדריך הנוכחי של הקבוצה (לא ניתן לבחירה)'
                            : instructor.email}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </ScrollView>
              </View>
            ) : null}

            <View style={styles.modalActionsWrap}>
              <PrimaryButton
                label={isUpdatingGroup ? 'שומר...' : 'שמירת שינויים'}
                style={styles.modalPrimaryButtonShell}
                gradientStyle={styles.modalPrimaryButton}
                textStyle={styles.modalPrimaryButtonText}
                colorsOverride={['#2E77BC', '#255E97']}
                onPress={updateGroup}
                disabled={isUpdatingGroup}
              />

              <TouchableOpacity style={styles.modalCancelButton} activeOpacity={0.8} onPress={closeEditGroupModal}>
                <Text style={styles.modalCancelButtonText}>ביטול</Text>
              </TouchableOpacity>
            </View>
            </ScrollView>
          </View>
        </View>
      </Modal>
    </View>
  );
}