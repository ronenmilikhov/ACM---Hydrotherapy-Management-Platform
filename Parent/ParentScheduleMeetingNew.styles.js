import { StyleSheet, StatusBar } from 'react-native';

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#F2F6FA',
  },
  aquaticBackground: {
    ...StyleSheet.absoluteFillObject,
  },

  bigCircleLeft: {
    position: 'absolute',
    width: 116,
    height: 116,
    borderRadius: 58,
    left: -28,
    top: 50,
    backgroundColor: 'rgba(217, 237, 251, 0.85)',
  },
  bigCircleRight: {
    position: 'absolute',
    width: 188,
    height: 188,
    borderRadius: 94,
    right: -70,
    top: -24,
    backgroundColor: 'rgba(180, 220, 246, 0.55)',
  },

  header: {
    paddingTop: (StatusBar.currentHeight || 0) + 14,
    paddingHorizontal: 16,
    flexDirection: 'row',
    alignItems: 'center',
  },
  backButton: {
    width: 44,
    height: 44,
    alignItems: 'center',
    justifyContent: 'center',
  },
  backButtonIcon: {
    fontSize: 42,
    lineHeight: 44,
    color: '#121212',
  },
  headerCenter: {
    flex: 1,
    alignItems: 'center',
    paddingRight: 20,
  },
  headerTitle: {
    color: '#204A78',
    fontSize: 30,
    fontWeight: '700',
    writingDirection: 'rtl',
  },

  scrollContent: {
    paddingHorizontal: 20,
    paddingTop: 18,
    paddingBottom: 30,
  },
  mainCard: {
    backgroundColor: '#FFFFFF',
    borderRadius: 24,
    borderWidth: 1,
    borderColor: 'rgba(51, 110, 163, 0.12)',
    paddingHorizontal: 16,
    paddingVertical: 18,
    minHeight: 520,
  },
  sectionTitle: {
    color: '#204A78',
    fontSize: 24,
    fontWeight: '700',
    writingDirection: 'rtl',
    alignSelf: 'flex-end',
    marginBottom: 12,
  },

  fieldCard: {
    backgroundColor: '#F8FBFF',
    borderRadius: 12,
    minHeight: 56,
    flexDirection: 'row-reverse',
    alignItems: 'center',
    paddingHorizontal: 10,
    marginBottom: 10,
  },
  fieldIconWrap: {
    width: 34,
    height: 34,
    borderRadius: 8,
    backgroundColor: '#DDE7F2',
    alignItems: 'center',
    justifyContent: 'center',
    marginLeft: 10,
  },
  fieldIcon: {
    color: '#7FB8F2',
    fontSize: 18,
    fontWeight: '700',
  },
  fieldTextWrap: {
    flex: 1,
    alignItems: 'flex-end',
  },
  fieldLabel: {
    color: '#8B99A7',
    fontSize: 14,
    writingDirection: 'rtl',
  },
  fieldValue: {
    marginTop: 2,
    color: '#1F1F1F',
    fontSize: 18,
    writingDirection: 'rtl',
  },

  notesWrap: {
    marginTop: 10,
  },
  notesLabel: {
    color: '#8B99A7',
    fontSize: 14,
    writingDirection: 'rtl',
    alignSelf: 'flex-end',
    marginBottom: 6,
  },
  notesInput: {
    backgroundColor: '#F8FBFF',
    borderRadius: 12,
    minHeight: 98,
    paddingHorizontal: 12,
    paddingTop: 10,
    fontSize: 16,
    color: '#1F1F1F',
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  submitButtonShell: {
    marginTop: 16,
    borderRadius: 14,
    overflow: 'hidden',
  },
  submitButton: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 12,
    borderRadius: 28,
  },
  submitButtonText: {
    color: '#EFF9FF',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
});

export default styles;