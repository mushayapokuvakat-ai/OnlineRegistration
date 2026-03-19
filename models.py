from datetime import datetime
from flask_sqlalchemy import SQLAlchemy

db = SQLAlchemy()


class College(db.Model):
    __tablename__ = 'colleges'
    college_id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(150), unique=True, nullable=False)
    code = db.Column(db.String(10), unique=True, nullable=False)
    description = db.Column(db.Text, default='')
    icon = db.Column(db.String(10), default='🏛️')

    programmes = db.relationship('Programme', backref='college', lazy=True)

    def to_dict(self):
        return {
            'collegeID': self.college_id,
            'name': self.name,
            'code': self.code,
            'description': self.description,
            'icon': self.icon,
            'programmeCount': len(self.programmes)
        }


class Programme(db.Model):
    __tablename__ = 'programmes'
    programme_id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(150), nullable=False)
    code = db.Column(db.String(20), unique=True, nullable=False)
    college_id = db.Column(db.Integer, db.ForeignKey('colleges.college_id'), nullable=False)

    courses = db.relationship('Course', backref='programme', lazy=True)

    def to_dict(self):
        return {
            'programmeID': self.programme_id,
            'name': self.name,
            'code': self.code,
            'collegeID': self.college_id
        }


class User(db.Model):
    __tablename__ = 'users'
    user_id = db.Column(db.Integer, primary_key=True)
    full_name = db.Column(db.String(100), nullable=False)
    email = db.Column(db.String(100), unique=True, nullable=False)
    password_hash = db.Column(db.String(255), nullable=False)
    course = db.Column(db.String(100), default='')
    role = db.Column(db.String(20), default='Student')
    college_id = db.Column(db.Integer, db.ForeignKey('colleges.college_id'), nullable=True)
    programme_id = db.Column(db.Integer, db.ForeignKey('programmes.programme_id'), nullable=True)
    level = db.Column(db.String(5), nullable=True)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    college_rel = db.relationship('College', backref='users', lazy=True)
    programme_rel = db.relationship('Programme', backref='users', lazy=True)

    def to_dict(self):
        return {
            'userID': self.user_id,
            'fullName': self.full_name,
            'email': self.email,
            'role': self.role,
            'course': self.course,
            'collegeID': self.college_id,
            'collegeName': self.college_rel.name if self.college_rel else None,
            'programmeID': self.programme_id,
            'programmeName': self.programme_rel.name if self.programme_rel else None,
            'level': self.level,
            'createdAt': self.created_at.isoformat()
        }


class Course(db.Model):
    __tablename__ = 'courses'
    course_id = db.Column(db.Integer, primary_key=True)
    course_name = db.Column(db.String(100), nullable=False)
    course_code = db.Column(db.String(20), unique=True, nullable=False)
    description = db.Column(db.Text, default='')
    lecturer_id = db.Column(db.Integer, db.ForeignKey('users.user_id'), nullable=True)
    lecturer_name = db.Column(db.String(100), default='')
    max_capacity = db.Column(db.Integer, default=100)
    is_active = db.Column(db.Boolean, default=True)
    enrolled_count = db.Column(db.Integer, default=0)
    programme_id = db.Column(db.Integer, db.ForeignKey('programmes.programme_id'), nullable=True)
    level = db.Column(db.String(5), nullable=True)

    def to_dict(self):
        return {
            'courseID': self.course_id,
            'courseName': self.course_name,
            'courseCode': self.course_code,
            'description': self.description,
            'lecturerID': self.lecturer_id,
            'lecturerName': self.lecturer_name,
            'maxCapacity': self.max_capacity,
            'isActive': self.is_active,
            'enrolledCount': self.enrolled_count,
            'programmeID': self.programme_id,
            'level': self.level
        }


class Enrollment(db.Model):
    __tablename__ = 'enrollments'
    enrollment_id = db.Column(db.Integer, primary_key=True)
    student_id = db.Column(db.Integer, db.ForeignKey('users.user_id'), nullable=False)
    student_name = db.Column(db.String(100), nullable=False)
    student_email = db.Column(db.String(100), nullable=False)
    course_id = db.Column(db.Integer, db.ForeignKey('courses.course_id'), nullable=False)
    course_name = db.Column(db.String(100), nullable=False)
    course_code = db.Column(db.String(20), nullable=False)
    enrolled_at = db.Column(db.DateTime, default=datetime.utcnow)

    def to_dict(self):
        return {
            'enrollmentID': self.enrollment_id,
            'studentID': self.student_id,
            'studentName': self.student_name,
            'studentEmail': self.student_email,
            'courseID': self.course_id,
            'courseName': self.course_name,
            'courseCode': self.course_code,
            'enrolledAt': self.enrolled_at.isoformat()
        }


class Setting(db.Model):
    __tablename__ = 'settings'
    key = db.Column(db.String(50), primary_key=True)
    value = db.Column(db.String(200), nullable=False)

    def to_dict(self):
        return {self.key: self.value}
